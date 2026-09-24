using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using DurableTask.Core.Exceptions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OIM.Moteur.Definitions;
using OIM.Moteur.Expressions;
using OIM.Moteur.Pilotage;

namespace OIM.Moteur.Orchestration;

/// <summary>
/// Interpréteur générique : une seule classe d'orchestration exécute toutes les définitions YAML.
/// Le nom d'orchestration DurableTask est l'id du processus et la version, sa version déployée.
///
/// <para>Déterminisme : la définition est chargée par une activité (résultat rejoué depuis
/// l'historique), l'heure provient de <see cref="OrchestrationContext.CurrentUtcDateTime"/> et les
/// expressions sont pures. Toute la logique peut donc être rejouée sans effet de bord.</para>
///
/// <para>Contexte des expressions :
/// <c>entrees</c>, <c>etapes.&lt;id&gt;.sortie</c>, <c>evenement</c> (dernier reçu), <c>variables</c>,
/// <c>erreur</c> (après un siErreur), <c>instance</c>, <c>maintenant</c>.</para>
/// </summary>
public sealed class OrchestrationProcessus(ILogger<OrchestrationProcessus> journal) : TaskOrchestration
{
    private const int TailleParcoursMax = 200;

    private readonly Dictionary<string, Queue<string?>> _tampon = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskCompletionSource<string?>> _attentes = new(StringComparer.OrdinalIgnoreCase);
    private readonly StatutProcessus _statut = new();

    // Mode test : mocks par étape et nombre de passages (une liste de mocks est consommée dans l'ordre).
    private JsonObject? _mocks;
    private readonly Dictionary<string, int> _passagesMock = new(StringComparer.Ordinal);
    private bool EnTest => _mocks is not null;

    // Id sous lequel le paquet est stocké (celui du YAML, ou d'un brouillon « ~… » en test) :
    // c'est lui qui permet aux activités de retrouver les gabarits.
    private string _idStockage = string.Empty;

    public override async Task<string> Execute(OrchestrationContext context, string input)
    {
        var entree = EntreeOrchestration.Lire(input);
        _idStockage = entree.DefinitionId;
        if (entree.Test is { } test) _mocks = test["mocks"] as JsonObject ?? [];
        var definition = await ChargerDefinitionAsync(context, entree.DefinitionId, entree.Version);

        var (entrees, erreurs) = ValidateurEntrees.Normaliser(definition, entree.Entrees);
        if (erreurs.Count > 0)
            throw Echouer(string.Join(" ", erreurs));

        var ctx = new JsonObject
        {
            ["entrees"] = entrees,
            ["etapes"] = new JsonObject(),
            ["variables"] = new JsonObject(),
            ["evenement"] = null,
            ["erreur"] = null,
            ["instance"] = new JsonObject
            {
                ["id"] = context.OrchestrationInstance.InstanceId,
                ["processus"] = definition.Id,
                ["version"] = entree.Version,
                ["debut"] = Iso(context.CurrentUtcDateTime)
            }
        };

        var courante = definition.EtapeInitiale?.Id;
        string? derniere = null;

        while (courante is not null)
        {
            var etape = definition.TrouverEtape(courante) ?? throw Echouer($"Étape « {courante} » introuvable.");
            derniere = etape.Id;

            if (++_statut.Transitions > definition.TransitionsMax)
                throw Echouer($"Plus de {definition.TransitionsMax} transitions : boucle probable (limites.transitions).");

            ctx["maintenant"] = Iso(context.CurrentUtcDateTime);
            EntrerDansEtape(context, etape, ctx);

            ResultatEtape resultat;
            try
            {
                resultat = await ExecuterEtapeAsync(context, definition, entree.Version, entree.VersionMoteur, etape, ctx);
            }
            catch (Exception ex) when (EstErreurEtape(ex))
            {
                var message = MessageErreur(ex);
                if (!context.IsReplaying)
                    journal.LogWarning("[{Instance}] Étape {Etape} en erreur : {Message}", context.OrchestrationInstance.InstanceId, etape.Id, message);

                ctx["etapes"]![etape.Id] = new JsonObject { ["erreur"] = message };
                if (etape.SiErreur is { } cible)
                {
                    ctx["erreur"] = new JsonObject { ["etape"] = etape.Id, ["message"] = message };
                    courante = cible;
                    continue;
                }
                throw Echouer($"Étape « {etape.Id} » : {message}");
            }

            var trace = new JsonObject { ["sortie"] = resultat.Sortie?.DeepClone() };
            if (resultat.Details is not null)
                foreach (var (cle, valeur) in resultat.Details) trace[cle] = valeur?.DeepClone();
            ctx["etapes"]![etape.Id] = trace;

            if (etape.Message is { } gabaritMessage)
                _statut.Message = Gabarit.Interpoler(gabaritMessage, ctx);

            courante = resultat.SuivanteForcee ?? ChoisirSuivante(etape, ctx);
        }

        _statut.Attente = null;
        _statut.MiseAJour = context.CurrentUtcDateTime;

        var sorties = new JsonObject();
        foreach (var (id, trace) in ctx["etapes"]!.AsObject())
            if (trace?["sortie"] is { } s) sorties[id] = s.DeepClone();

        return new JsonObject
        {
            ["etapeFinale"] = derniere,
            ["statut"] = _statut.Statut,
            ["message"] = _statut.Message,
            ["variables"] = ctx["variables"]!.DeepClone(),
            ["sorties"] = sorties
        }.ToJsonString();
    }

    public override void RaiseEvent(OrchestrationContext context, string name, string input)
    {
        if (_attentes.Remove(name, out var attente))
        {
            attente.TrySetResult(input);
            return;
        }

        // Événement reçu alors qu'on ne l'attend pas (ex. pendant une relance) : on le garde
        // pour la prochaine attente de ce nom.
        if (!_tampon.TryGetValue(name, out var file)) _tampon[name] = file = new Queue<string?>();
        file.Enqueue(input);
    }

    public override string GetStatus() => JsonSerializer.Serialize(_statut, Json.Options);

    // ── Étapes ───────────────────────────────────────────────────────────────

    private sealed record ResultatEtape(JsonNode? Sortie, string? SuivanteForcee = null, JsonObject? Details = null);

    private void EntrerDansEtape(OrchestrationContext context, Etape etape, JsonObject ctx)
    {
        _statut.Etape = etape.Id;
        _statut.TypeEtape = etape.Type;
        _statut.Attente = null;
        _statut.MiseAJour = context.CurrentUtcDateTime;
        if (etape.Statut is { } statut) _statut.Statut = Gabarit.Interpoler(statut, ctx);

        _statut.Parcours.Add(etape.Id);
        if (_statut.Parcours.Count > TailleParcoursMax) _statut.Parcours.RemoveAt(0);

        if (!context.IsReplaying)
            journal.LogInformation("[{Instance}] → {Etape} ({Type})", context.OrchestrationInstance.InstanceId, etape.Id, etape.Type);
    }

    private async Task<ResultatEtape> ExecuterEtapeAsync(OrchestrationContext context, DefinitionProcessus definition,
        int version, int versionMoteur, Etape etape, JsonObject ctx)
    {
        switch (etape.Type)
        {
            case CatalogueEtapes.Http:
            {
                var parametres = new JsonObject
                {
                    ["definitionId"] = _idStockage,
                    ["version"] = version,
                    ["requete"] = Texte(etape, "requete", ctx),
                    ["donnees"] = etape.Valeur("donnees") is { } donnees ? Gabarit.Resoudre(donnees, ctx) : ctx.DeepClone(),
                    ["correlation"] = context.OrchestrationInstance.InstanceId,
                    ["cle"] = CleIdempotence(context, etape)
                };
                if (EnTest)
                {
                    // Le gabarit est chargé et la requête construite, mais c'est le mock qui répond.
                    parametres["test"] = true;
                    parametres["etape"] = etape.Id;
                    parametres["mock"] = MockPour(etape.Id);
                }
                var reponse = await AppelerActiviteAsync(context, NomsActivites.Http, parametres, Reprise(etape));
                return new ResultatEtape(reponse?["corps"], Details: new JsonObject { ["statutHttp"] = reponse?["statut"]?.DeepClone() });
            }

            case CatalogueEtapes.Courriel:
            {
                var parametres = new JsonObject
                {
                    ["definitionId"] = _idStockage,
                    ["version"] = version,
                    ["gabarit"] = Texte(etape, "gabarit", ctx),
                    ["a"] = Gabarit.Resoudre(etape.Valeur("a"), ctx),
                    ["cc"] = Gabarit.Resoudre(etape.Valeur("cc"), ctx),
                    ["cci"] = Gabarit.Resoudre(etape.Valeur("cci"), ctx),
                    ["contexte"] = FusionnerDonnees(ctx, Gabarit.Resoudre(etape.Valeur("donnees"), ctx)),
                    ["cle"] = CleIdempotence(context, etape),
                    ["langue"] = Texte(etape, "langue", ctx)
                };
                if (EnTest) parametres["test"] = true;  // rendu complet du gabarit, sans envoi
                return new ResultatEtape(await AppelerActiviteAsync(context, NomsActivites.Courriel, parametres, Reprise(etape)));
            }

            case var type when CatalogueEtapes.TypesService.Contains(type):
            {
                // Propriétés résolues transmises telles quelles au service du type (voir ServiceActivite).
                var parametres = new JsonObject
                {
                    ["service"] = type,
                    ["etape"] = etape.Id,
                    ["parametres"] = Gabarit.Resoudre(etape.Proprietes, ctx),
                    ["correlation"] = context.OrchestrationInstance.InstanceId,
                    ["cle"] = CleIdempotence(context, etape)
                };
                if (EnTest)
                {
                    parametres["test"] = true;
                    parametres["mock"] = MockPour(etape.Id);
                }
                return new ResultatEtape(await AppelerActiviteAsync(context, NomsActivites.Service, parametres, Reprise(etape)));
            }

            case CatalogueEtapes.BoiteGenerique:
                return await BoiteGeneriqueAsync(context, version, etape, ctx);

            case CatalogueEtapes.AttendreEvenement:
            {
                var evenement = Texte(etape, "evenement", ctx) ?? throw new ErreurProcessus("Nom d'événement vide.");
                TimeSpan? delai = Texte(etape, "delai", ctx) is { } d ? Duree.Lire(d, $"Étape {etape.Id}") : null;

                _statut.Attente = new AttenteProcessus
                {
                    Evenement = evenement,
                    Echeance = delai is { } t ? context.CurrentUtcDateTime + t : null
                };

                var (expire, donnees) = await AttendreEvenementAsync(context, evenement, delai);
                _statut.Attente = null;

                if (expire)
                {
                    var siExpire = etape.Texte("siDelaiExpire")
                                   ?? throw new ErreurProcessus($"Délai expiré en attente de l'événement « {evenement} ».");
                    return new ResultatEtape(null, siExpire, new JsonObject { ["delaiExpire"] = true });
                }

                var charge = Json.Lire(donnees);
                ctx["evenement"] = charge?.DeepClone();
                return new ResultatEtape(charge, Details: new JsonObject { ["delaiExpire"] = false, ["recuLe"] = Iso(context.CurrentUtcDateTime) });
            }

            case CatalogueEtapes.Delai:
            {
                DateTime echeance;
                if (Texte(etape, "duree", ctx) is { } duree)
                    echeance = context.CurrentUtcDateTime + Duree.Lire(duree, $"Étape {etape.Id}");
                else if (Texte(etape, "jusqua", ctx) is { } jusqua
                         && DateTimeOffset.TryParse(jusqua, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                    echeance = date.UtcDateTime;
                else
                    throw new ErreurProcessus("Durée ou date d'échéance invalide.");

                if (echeance > context.CurrentUtcDateTime && !EnTest)
                {
                    _statut.Attente = new AttenteProcessus { Echeance = echeance };
                    await context.CreateTimer(echeance, string.Empty);
                    _statut.Attente = null;
                }
                return new ResultatEtape(null, Details: new JsonObject { ["echeance"] = Iso(echeance) });
            }

            case CatalogueEtapes.Decision:
                return new ResultatEtape(null);

            case CatalogueEtapes.Reponse:
            {
                // La réponse est publiée dans le statut personnalisé, persisté au point de contrôle
                // qui précède l'activité ci-dessous : l'appelant (?attendre=) la reçoit sans délai.
                var (statutHttp, corps) = ReponseAppelant.Evaluer(etape.Valeur("statutHttp"), etape.Valeur("corps"), ctx);
                _statut.Reponse = new ReponseProcessus
                {
                    StatutHttp = statutHttp,
                    Corps = corps?.DeepClone(),
                    Etape = etape.Id,
                    EmiseLe = context.CurrentUtcDateTime
                };

                // Trace dans l'historique DurableTask : la planification porte le gabarit et le contexte
                // reçu, le résultat porte la réponse produite (même évaluation, pure et déterministe).
                // Seulement pour les instances démarrées avec le moteur v2+ (déterminisme des relectures).
                if (versionMoteur >= 2)
                    await AppelerActiviteAsync(context, NomsActivites.Reponse, new JsonObject
                    {
                        ["etape"] = etape.Id,
                        ["statutHttp"] = etape.Valeur("statutHttp")?.DeepClone(),
                        ["corps"] = etape.Valeur("corps")?.DeepClone(),
                        ["contexte"] = ctx.DeepClone()
                    }, null);
                return new ResultatEtape(corps, Details: new JsonObject { ["statutHttp"] = statutHttp });
            }

            case CatalogueEtapes.Definir:
            {
                var valeurs = Gabarit.Resoudre(etape.Valeur("variables"), ctx) as JsonObject ?? [];
                var variables = ctx["variables"]!.AsObject();
                foreach (var (cle, valeur) in valeurs) variables[cle] = valeur?.DeepClone();
                return new ResultatEtape(valeurs);
            }

            case CatalogueEtapes.SousProcessus:
            {
                // Le processus enfant est toujours celui de l'équipe du parent (id qualifié « equipe.processus »).
                var processus = IdsEquipe.Qualifier(IdsEquipe.Equipe(_idStockage) ?? throw new ErreurProcessus("Équipe du processus inconnue."),
                    Texte(etape, "processus", ctx) ?? throw new ErreurProcessus("Processus enfant non précisé."));
                if (EnTest)
                    return new ResultatEtape(MockPour(etape.Id)
                        ?? throw new ErreurProcessus($"Mode test : un mock est requis pour le sous-processus « {etape.Id} » (sortie du processus enfant)."));
                int? versionEnfant = Texte(etape, "version", ctx) is { } v && int.TryParse(v, out var n) ? n : null;
                var charge = await ChargerAsync(context, processus, versionEnfant);
                var versionResolue = charge["version"]!.GetValue<int>();

                var entreeEnfant = new EntreeOrchestration
                {
                    DefinitionId = processus,
                    Version = versionResolue,
                    Entrees = Gabarit.Resoudre(etape.Valeur("entrees"), ctx) as JsonObject ?? []
                };
                var idEnfant = $"{context.OrchestrationInstance.InstanceId}:{etape.Id}:{_statut.Transitions}";

                var sortie = etape.Retry is { } r
                    ? await context.CreateSubOrchestrationInstanceWithRetry<JToken>(processus, versionResolue.ToString(CultureInfo.InvariantCulture),
                        idEnfant, OptionsReprise(r), Json.Brut(entreeEnfant.VersJson()))
                    : await context.CreateSubOrchestrationInstance<JToken>(processus, versionResolue.ToString(CultureInfo.InvariantCulture),
                        idEnfant, Json.Brut(entreeEnfant.VersJson()));

                return new ResultatEtape(Json.DepuisJToken(sortie), Details: new JsonObject { ["instanceEnfant"] = idEnfant });
            }

            default:
                throw new ErreurProcessus($"Type d'étape « {etape.Type} » non pris en charge.");
        }
    }

    /// <summary>
    /// Premier bloc dont la condition <c>si</c> est vraie (ou sans condition) : adresse résolue (bloc ou table
    /// BSQ du paquet), puis courriel au gabarit du bloc ou de l'étape. Aucun bloc applicable : rien n'est envoyé.
    /// </summary>
    private async Task<ResultatEtape> BoiteGeneriqueAsync(OrchestrationContext context, int version, Etape etape, JsonObject ctx)
    {
        var blocs = etape.Valeur("blocs") as JsonArray ?? throw new ErreurProcessus("« blocs » doit être une liste.");
        var index = -1;
        JsonObject? bloc = null;
        for (var i = 0; i < blocs.Count && bloc is null; i++)
        {
            if (blocs[i] is not JsonObject candidat) continue;
            if (candidat["si"] is { } si && !Gabarit.Condition(Expression.EnTexte(si), ctx)) continue;
            index = i;
            bloc = Gabarit.Resoudre(candidat, ctx) as JsonObject;
        }
        if (bloc is null) return new ResultatEtape(new JsonObject { ["envoye"] = false });

        var boite = await AppelerActiviteAsync(context, NomsActivites.ResoudreBoite, new JsonObject
        {
            ["definitionId"] = _idStockage,
            ["version"] = version,
            ["index"] = index,
            ["bloc"] = bloc.DeepClone()
        }, null) as JsonObject ?? throw new ErreurProcessus("Boîte générique non résolue.");

        var donnees = new JsonObject { ["boite"] = boite.DeepClone() };
        if (Gabarit.Resoudre(etape.Valeur("donnees"), ctx) is JsonObject extra)
            foreach (var (cle, valeur) in extra) donnees[cle] = valeur?.DeepClone();

        var parametres = new JsonObject
        {
            ["definitionId"] = _idStockage,
            ["version"] = version,
            ["gabarit"] = Expression.EnTexte(bloc["gabarit"]) is { Length: > 0 } g ? g : Texte(etape, "gabarit", ctx),
            ["a"] = boite["a"]?.DeepClone(),
            ["contexte"] = FusionnerDonnees(ctx, donnees),
            ["cle"] = CleIdempotence(context, etape),
            ["langue"] = Texte(etape, "langue", ctx)
        };
        if (EnTest) parametres["test"] = true;
        var courriel = await AppelerActiviteAsync(context, NomsActivites.Courriel, parametres, Reprise(etape));

        var sortie = boite.DeepClone().AsObject();
        sortie["envoye"] = true;
        sortie["courriel"] = courriel?.DeepClone();
        return new ResultatEtape(sortie);
    }

    private static string? ChoisirSuivante(Etape etape, JsonObject ctx)
    {
        if (etape.Fin) return null;
        foreach (var branche in etape.Suivant)
            if (branche.Condition is null || Gabarit.Condition(branche.Condition, ctx))
                return branche.Aller;
        return null;
    }

    // ── Événements externes (WaitForExternalEvent + CreateTimer) ─────────────

    private async Task<(bool Expire, string? Donnees)> AttendreEvenementAsync(OrchestrationContext context, string nom, TimeSpan? delai)
    {
        var attente = Attendre(nom);

        if (EnTest)
        {
            // Mode test : aucune minuterie; l'expiration est simulée par le scénario du cas de test.
            var expiration = Attendre(NomsActivites.ExpirationSimulee);
            var premiere = await Task.WhenAny((Task)attente, expiration);
            if (premiere == attente)
            {
                _attentes.Remove(NomsActivites.ExpirationSimulee);
                return (false, await attente);
            }
            _attentes.Remove(nom);
            return (true, null);
        }

        if (attente.IsCompleted || delai is null)
            return (false, await attente);

        using var annulation = new CancellationTokenSource();
        var minuterie = context.CreateTimer(context.CurrentUtcDateTime + delai.Value, string.Empty, annulation.Token);
        var gagnant = await Task.WhenAny((Task)attente, minuterie);

        if (gagnant == attente)
        {
            annulation.Cancel();
            return (false, await attente);
        }

        _attentes.Remove(nom);
        return (true, null);
    }

    /// <summary>Événement déjà reçu (tampon), sinon attente enregistrée pour <see cref="RaiseEvent"/>.</summary>
    private Task<string?> Attendre(string nom)
    {
        if (_tampon.TryGetValue(nom, out var file) && file.Count > 0)
            return Task.FromResult(file.Dequeue());

        var attente = new TaskCompletionSource<string?>();
        _attentes[nom] = attente;
        return attente.Task;
    }

    // ── Mode test ────────────────────────────────────────────────────────────

    /// <summary>Mock de l'étape : objet unique, ou liste consommée à chaque passage (le dernier se répète).</summary>
    private JsonNode? MockPour(string etape)
    {
        if (_mocks?[etape] is not { } mock) return null;
        var passage = _passagesMock.TryGetValue(etape, out var n) ? n : 0;
        _passagesMock[etape] = passage + 1;
        return mock is JsonArray liste
            ? liste.Count == 0 ? null : liste[Math.Min(passage, liste.Count - 1)]?.DeepClone()
            : mock.DeepClone();
    }

    /// <summary>En test, les reprises gardent leur nombre de tentatives mais sans les délais réels.</summary>
    private PolitiqueReprise? Reprise(Etape etape) =>
        EnTest && etape.Retry is { } r
            ? r with { Delai = TimeSpan.FromMilliseconds(100), DelaiMax = TimeSpan.FromMilliseconds(200) }
            : etape.Retry;

    // ── Activités ────────────────────────────────────────────────────────────

    private static async Task<DefinitionProcessus> ChargerDefinitionAsync(OrchestrationContext context, string id, int version)
    {
        var charge = await ChargerAsync(context, id, version);
        var lecture = LecteurDefinition.Lire(charge["yaml"]!.GetValue<string>());
        if (!lecture.Valide)
            throw new ErreurProcessus($"Définition « {id} » v{version} invalide : {string.Join(" ", lecture.Erreurs.Select(e => e.Message))}");
        return lecture.Definition!;
    }

    private static async Task<JsonObject> ChargerAsync(OrchestrationContext context, string id, int? version) =>
        await AppelerActiviteAsync(context, NomsActivites.ChargerDefinition,
            new JsonObject { ["definitionId"] = id, ["version"] = version }, null) as JsonObject
        ?? throw new ErreurProcessus($"Processus « {id} » introuvable.");

    private static async Task<JsonNode?> AppelerActiviteAsync(OrchestrationContext context, string nom,
        JsonObject parametres, PolitiqueReprise? reprise)
    {
        var entree = parametres.ToJsonString();
        var resultat = reprise is null
            ? await context.ScheduleTask<string>(nom, string.Empty, entree)
            : await context.ScheduleWithRetry<string>(nom, string.Empty, OptionsReprise(reprise), entree);
        return Json.Lire(resultat);
    }

    private static RetryOptions OptionsReprise(PolitiqueReprise r)
    {
        var options = new RetryOptions(r.Delai, r.Tentatives) { BackoffCoefficient = r.Backoff };
        if (r.DelaiMax is { } max) options.MaxRetryInterval = max;
        return options;
    }

    // ── Utilitaires ──────────────────────────────────────────────────────────

    /// <summary>
    /// Clé stable d'une exécution d'étape : identique pour les reprises (retry) et pour une
    /// réexécution de l'activité après la panne d'un serveur (livraison « au moins une fois »),
    /// différente si l'étape est revisitée (boucle). Les services appelés peuvent s'en servir
    /// pour ignorer un doublon.
    /// </summary>
    private string CleIdempotence(OrchestrationContext context, Etape etape) =>
        $"{context.OrchestrationInstance.InstanceId}:{etape.Id}:{_statut.Transitions}";

    private static string? Texte(Etape etape, string propriete, JsonObject ctx) =>
        etape.Valeur(propriete) is { } v ? Expression.EnTexte(Gabarit.Resoudre(v, ctx)) is { Length: > 0 } s ? s : null : null;

    private static JsonObject FusionnerDonnees(JsonObject ctx, JsonNode? donnees)
    {
        var resultat = ctx.DeepClone().AsObject();
        if (donnees is JsonObject o)
            foreach (var (cle, valeur) in o) resultat[cle] = valeur?.DeepClone();
        return resultat;
    }

    private static bool EstErreurEtape(Exception ex) =>
        ex is TaskFailedException or SubOrchestrationFailedException or ErreurProcessus or ErreurExpression or FormatException;

    private static string MessageErreur(Exception ex) => ex switch
    {
        TaskFailedException { FailureDetails: { } f } => f.ErrorMessage,
        SubOrchestrationFailedException { FailureDetails: { } f } => f.ErrorMessage,
        _ => ex.InnerException?.Message ?? ex.Message
    };

    private ErreurProcessus Echouer(string message)
    {
        _statut.Erreur = message;
        _statut.Attente = null;
        return new ErreurProcessus(message);
    }

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);
}
