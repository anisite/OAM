using System.Diagnostics;
using System.Text.Json.Nodes;
using DurableTask.Core;
using DurableTask.SqlServer;
using Microsoft.Extensions.Logging;
using OIM.Moteur.Definitions;
using OIM.Moteur.Orchestration;
using OIM.Moteur.Pilotage;
using OIM.Moteur.Stockage;

namespace OIM.Moteur.Tests;

public sealed record ResultatCas(
    string Fichier,
    string Nom,
    bool Reussi,
    IReadOnlyList<string> Ecarts,
    long DureeMs,
    string? InstanceId,
    JsonObject? Obtenu);

public sealed record RapportTests(string Processus, IReadOnlyList<ResultatCas> Cas, IReadOnlyList<Diagnostic> Diagnostics)
{
    public int Reussis => Cas.Count(c => c.Reussi);
    public int Echoues => Cas.Count(c => !c.Reussi) + (Diagnostics.Any(d => d.Gravite == Diagnostic.Erreur) ? 1 : 0);
    public bool Reussi => Echoues == 0;
}

/// <summary>
/// Exécute les cas de test métier d'un paquet sur le vrai moteur DurableTask :
/// le paquet est enregistré comme brouillon (lisible par tous les nœuds), chaque cas démarre une
/// instance en mode test (mocks, aucun appel ni envoi réel, délais simulés), le scénario est joué
/// comme le ferait le monde extérieur, puis l'état obtenu est comparé à l'attendu.
/// Les instances et le brouillon sont supprimés à la fin (sauf <c>conserver</c>, pour inspection).
/// </summary>
public sealed class ServiceTests(
    IDepotDefinitions depot,
    TaskHubClient client,
    SqlOrchestrationService service,
    RequetesSuivi suivi,
    ILogger<ServiceTests> journal)
{
    private const int Parallelisme = 4;

    /// <param name="equipe">Équipe du paquet : préfixe du brouillon et des instances de test.</param>
    public async Task<RapportTests> ExecuterAsync(string equipe, PaquetDefinition paquet, string? filtre = null, bool conserver = false,
        CancellationToken ct = default)
    {
        var validation = ValidateurDefinition.Valider(paquet);
        var id = validation.Definition?.Id ?? "?";
        if (!validation.Valide)
            return new RapportTests(id, [], validation.Erreurs.ToList());

        var cas = CasTest.Trouver(paquet)
            .Where(c => filtre is null
                        || string.Equals(Path.GetFileNameWithoutExtension(c.Chemin), filtre, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c.Chemin, filtre, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (cas.Count == 0) return new RapportTests(id, [], []);

        var brouillon = $"{IDepotDefinitions.PrefixeBrouillon}{IdsEquipe.Qualifier(equipe, id.Length > 60 ? id[..60] : id)}~{Guid.NewGuid().ToString("N")[..8]}";
        await depot.EnregistrerBrouillonAsync(brouillon, paquet, ct);
        try
        {
            using var limite = new SemaphoreSlim(Parallelisme);
            var resultats = await Task.WhenAll(cas.Select(async c =>
            {
                await limite.WaitAsync(ct);
                try { return await ExecuterCasAsync(equipe, brouillon, c.Chemin, c.Contenu, conserver, ct); }
                finally { limite.Release(); }
            }));

            var rapport = new RapportTests(id, resultats, []);
            journal.LogInformation("Tests « {Processus} » : {Reussis}/{Total} réussi(s).", id, rapport.Reussis, rapport.Cas.Count);
            return rapport;
        }
        finally
        {
            if (!conserver) await depot.SupprimerBrouillonAsync(brouillon, CancellationToken.None);
        }
    }

    private async Task<ResultatCas> ExecuterCasAsync(string equipe, string brouillon, string chemin, string contenu, bool conserver,
        CancellationToken ct)
    {
        CasTest cas;
        try
        {
            cas = CasTest.Lire(chemin, contenu);
        }
        catch (FormatException ex)
        {
            return new ResultatCas(chemin, Path.GetFileNameWithoutExtension(chemin), false, [$"Cas de test illisible : {ex.Message}"], 0, null, null);
        }

        var chrono = Stopwatch.StartNew();
        var instanceId = IdsEquipe.Qualifier(equipe, $"~test-{Guid.NewGuid():N}");
        var ecarts = new List<string>();
        JsonObject? obtenu = null;

        try
        {
            var entree = new EntreeOrchestration
            {
                DefinitionId = brouillon,
                Version = 1,
                Entrees = cas.Entrees,
                Test = new JsonObject { ["mocks"] = cas.Mocks.DeepClone() }
            };
            await Reessayer(() => client.CreateOrchestrationInstanceAsync(brouillon, "1", instanceId, Json.Brut(entree.VersJson())));

            var limite = DateTime.UtcNow + cas.DelaiMax;
            var transitions = 0;

            // Scénario : à chaque action, attendre que l'étape soit en attente, puis agir.
            foreach (var (action, i) in cas.Scenario.Select((a, i) => (a, i + 1)))
            {
                var etat = await AttendreAsync(instanceId, limite, transitions, ct);
                var statut = Statut(etat);

                if (etat is null || EstTerminal(etat))
                {
                    ecarts.Add($"scenario[{i}] : l'instance est {etat?.OrchestrationStatus.ToString() ?? "introuvable"} avant d'atteindre « {action.Attendre} »"
                               + (etat?.FailureDetails is { } f ? $" ({f.ErrorMessage})" : ""));
                    break;
                }
                if (statut?["attente"] is null)
                {
                    ecarts.Add($"scenario[{i}] : délai dépassé en attendant l'étape « {action.Attendre} » (étape courante : {statut?["etape"]}).");
                    break;
                }
                if (statut["etape"]?.GetValue<string>() != action.Attendre)
                {
                    ecarts.Add($"scenario[{i}] : l'instance attend à « {statut["etape"]} » et non à « {action.Attendre} ».");
                    break;
                }

                transitions = statut["transitions"]?.GetValue<int>() ?? 0;
                var instance = new OrchestrationInstance { InstanceId = instanceId };
                if (action.DelaiExpire)
                    await Reessayer(async () => { await client.RaiseEventAsync(instance, NomsActivites.ExpirationSimulee, Json.Brut(null)); return 0; });
                foreach (var (nom, donnees) in action.Evenements ?? [])
                    await Reessayer(async () => { await client.RaiseEventAsync(instance, nom, Json.Brut(donnees)); return 0; });
            }

            var final = await AttendreAsync(instanceId, limite, transitions, ct);
            obtenu = await InstantaneAsync(instanceId, final, ct);
            if (ecarts.Count == 0 && final is not null && !EstTerminal(final) && Statut(final)?["attente"] is null)
                ecarts.Add($"délai dépassé ({cas.DelaiMax.TotalSeconds:0} s) : l'instance n'a ni terminé ni atteint d'attente.");
            ecarts.AddRange(ComparateurAttendu.Comparer(cas.Attendu, obtenu));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ecarts.Add($"Erreur d'exécution du test : {ex.Message}");
        }
        finally
        {
            if (!conserver) await NettoyerAsync(instanceId);
        }

        return new ResultatCas(chemin, cas.Nom, ecarts.Count == 0, ecarts, chrono.ElapsedMilliseconds, conserver ? instanceId : null, obtenu);
    }

    /// <summary>
    /// Attend que l'instance soit terminée, ou en attente d'un événement après la transition
    /// <paramref name="apresTransition"/> (stable sur deux lectures), ou que le délai expire.
    /// </summary>
    private async Task<OrchestrationState?> AttendreAsync(string instanceId, DateTime limite, int apresTransition, CancellationToken ct)
    {
        OrchestrationState? etat = null;
        string? precedent = null;
        while (DateTime.UtcNow < limite)
        {
            etat = await Reessayer(() => client.GetOrchestrationStateAsync(instanceId));
            if (etat is not null)
            {
                if (EstTerminal(etat)) return etat;
                var statut = Statut(etat);
                var enAttente = etat.OrchestrationStatus == OrchestrationStatus.Running
                                && statut?["attente"]?["evenement"] is not null
                                && (statut["transitions"]?.GetValue<int>() ?? 0) > apresTransition;
                if (enAttente)
                {
                    if (precedent == etat.Status) return etat;
                    precedent = etat.Status;
                }
            }
            await Task.Delay(150, ct);
        }
        return etat;
    }

    private async Task<JsonObject> InstantaneAsync(string instanceId, OrchestrationState? etat, CancellationToken ct)
    {
        var statut = Statut(etat);
        var sortie = Json.Lire(etat?.Output) as JsonObject;
        var o = new JsonObject
        {
            ["statut"] = etat?.OrchestrationStatus.ToString(),
            ["etape"] = statut?["etape"]?.DeepClone(),
            ["attente"] = statut?["attente"]?["evenement"]?.DeepClone(),
            ["statutMetier"] = statut?["statut"]?.DeepClone(),
            ["message"] = statut?["message"]?.DeepClone(),
            ["parcours"] = statut?["parcours"]?.DeepClone() ?? new JsonArray(),
            ["reponse"] = statut?["reponse"] is JsonObject r
                ? new JsonObject { ["statutHttp"] = r["statutHttp"]?.DeepClone(), ["corps"] = r["corps"]?.DeepClone() }
                : null,
            ["etapeFinale"] = sortie?["etapeFinale"]?.DeepClone(),
            ["sorties"] = sortie?["sorties"]?.DeepClone(),
            ["variables"] = sortie?["variables"]?.DeepClone(),
            ["erreur"] = etat?.FailureDetails?.ErrorMessage
        };

        // Courriels « envoyés » (simulés), dans l'ordre, reconstitués depuis l'historique DurableTask.
        var historique = await Reessayer(() => suivi.HistoriqueAsync(Habilitations.Systeme, instanceId, ct, complet: true));
        var courriels = historique.Where(h => h.Type == "TaskScheduled" && h.Nom == NomsActivites.Courriel).Select(h => h.TacheId).ToHashSet();
        o["courriels"] = new JsonArray(historique
            .Where(h => h.Type == "TaskCompleted" && courriels.Contains(h.TacheId))
            .Select(h => Decoder(h.Donnees))
            .Where(c => c is not null)
            .ToArray());
        return o;
    }

    private async Task NettoyerAsync(string instanceId)
    {
        try
        {
            var etat = await client.GetOrchestrationStateAsync(instanceId);
            if (etat is null) return;
            if (!EstTerminal(etat))
            {
                await client.TerminateInstanceAsync(new OrchestrationInstance { InstanceId = instanceId }, "Fin du test");
                for (var i = 0; i < 40 && !EstTerminal((await client.GetOrchestrationStateAsync(instanceId))!); i++)
                    await Task.Delay(100);
            }
            await Reessayer(() => service.PurgeInstanceStateAsync(instanceId));
        }
        catch (Exception ex)
        {
            journal.LogWarning("Nettoyage de l'instance de test {Instance} impossible : {Message}", instanceId, ex.Message);
        }
    }

    /// <summary>
    /// Les cas s'exécutent en parallèle (création, purge, lecture d'état) : SQL Server peut désigner l'un
    /// d'eux comme victime d'interblocage (1205). L'opération est alors simplement rejouée.
    /// </summary>
    private static async Task<T> Reessayer<T>(Func<Task<T>> operation)
    {
        for (var tentative = 1; ; tentative++)
        {
            try
            {
                return await operation();
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205 && tentative < 5)
            {
                await Task.Delay(Random.Shared.Next(50, 250) * tentative);
            }
        }
    }

    /// <summary>Résultat d'activité : chaîne JSON contenant le JSON (voir <see cref="Json"/>).</summary>
    private static JsonNode? Decoder(string? donnees)
    {
        var n = Json.Lire(donnees);
        for (var i = 0; i < 3 && n is JsonValue v && v.TryGetValue<string>(out var s); i++) n = Json.Lire(s);
        return n;
    }

    private static JsonObject? Statut(OrchestrationState? e) => Json.Lire(e?.Status) as JsonObject;

    private static bool EstTerminal(OrchestrationState e) =>
        e.OrchestrationStatus is OrchestrationStatus.Completed or OrchestrationStatus.Failed
            or OrchestrationStatus.Terminated or OrchestrationStatus.Canceled;
}
