using System.Globalization;
using System.Text.Json.Nodes;
using DurableTask.Core;
using DurableTask.Core.Exceptions;
using DurableTask.SqlServer;
using OIM.Moteur.Definitions;
using OIM.Moteur.Orchestration;
using OIM.Moteur.Stockage;

namespace OIM.Moteur.Pilotage;

public sealed record ResultatDemarrage(string InstanceId, string Processus, int Version);

public sealed record ErreurInstance(string? Type, string Message, string? Pile);

public enum NatureAttente { Reponse, Terminee, Echec, DelaiDepasse }

/// <summary>Résultat d'un démarrage synchrone : réponse publiée, fin, échec ou délai dépassé.</summary>
public sealed record ResultatAttente(
    string InstanceId,
    string Statut,
    NatureAttente Nature,
    int StatutHttp,
    JsonNode? Corps,
    JsonObject? StatutPersonnalise);

public sealed record InstanceDetail(
    string InstanceId,
    string Processus,
    string? Version,
    string Statut,
    DateTime Creee,
    DateTime MiseAJour,
    DateTime? Terminee,
    string? ParentInstanceId,
    JsonNode? Entrees,
    JsonNode? Sortie,
    JsonNode? StatutPersonnalise,
    ErreurInstance? Erreur,
    IDictionary<string, string>? Etiquettes);

/// <summary>Actions sur les instances : démarrage, événements et commandes de pilotage.</summary>
public sealed class ServiceInstances(TaskHubClient client, SqlOrchestrationService service, IDepotDefinitions depot)
{
    private static readonly OrchestrationStatus[] StatutsActifs =
        [OrchestrationStatus.Pending, OrchestrationStatus.Running, OrchestrationStatus.Suspended, OrchestrationStatus.ContinuedAsNew];

    public async Task<ResultatDemarrage> DemarrerAsync(string processus, JsonObject? entrees, int? version = null,
        string? instanceId = null, string? demarrePar = null, CancellationToken ct = default)
    {
        var resume = (await depot.ListerAsync(ct)).FirstOrDefault(d => d.Id == processus)
                     ?? throw ErreurPilotage.Introuvable($"Processus « {processus} » introuvable.");
        if (!resume.Actif && version is null)
            throw ErreurPilotage.Conflit($"Le processus « {processus} » est désactivé.");

        var def = await depot.ObtenirAsync(processus, version, ct)
                  ?? throw ErreurPilotage.Introuvable($"Processus « {processus} » v{version} introuvable.");
        var lecture = LecteurDefinition.Lire(def.Yaml);
        var (normalisees, erreurs) = ValidateurEntrees.Normaliser(lecture.Definition!, entrees);
        if (erreurs.Count > 0) throw ErreurPilotage.Invalide("Entrées invalides.", erreurs);

        if (instanceId is not null && (instanceId.Length > 100 || instanceId.Any(char.IsWhiteSpace)))
            throw ErreurPilotage.Invalide("L'identifiant d'instance doit faire au plus 100 caractères, sans espace.");

        var id = instanceId ?? Guid.NewGuid().ToString("N");
        var entree = new EntreeOrchestration { DefinitionId = def.DefinitionId, Version = def.Version, Entrees = normalisees };
        var etiquettes = new Dictionary<string, string> { ["demarrePar"] = demarrePar ?? "anonyme" };

        try
        {
            await client.CreateOrchestrationInstanceAsync(def.DefinitionId, def.Version.ToString(CultureInfo.InvariantCulture),
                id, Json.Brut(entree.VersJson()), etiquettes, StatutsActifs);
        }
        catch (OrchestrationAlreadyExistsException)
        {
            throw ErreurPilotage.Conflit($"Une instance « {id} » est déjà en cours.");
        }

        return new ResultatDemarrage(id, def.DefinitionId, def.Version);
    }

    public async Task EnvoyerEvenementAsync(string instanceId, string nom, JsonNode? donnees)
    {
        var etat = await EtatActifAsync(instanceId);
        await client.RaiseEventAsync(etat.OrchestrationInstance, nom, Json.Brut(donnees));
    }

    public async Task TerminerAsync(string instanceId, string? raison)
    {
        var etat = await EtatActifAsync(instanceId);
        await client.TerminateInstanceAsync(etat.OrchestrationInstance, raison ?? "Interrompue depuis le tableau de bord.");
    }

    public async Task SuspendreAsync(string instanceId, string? raison)
    {
        var etat = await EtatActifAsync(instanceId);
        if (etat.OrchestrationStatus == OrchestrationStatus.Suspended) return;
        await client.SuspendInstanceAsync(etat.OrchestrationInstance, raison ?? "Suspendue depuis le tableau de bord.");
    }

    public async Task ReprendreAsync(string instanceId, string? raison)
    {
        var etat = await EtatAsync(instanceId);
        if (etat.OrchestrationStatus != OrchestrationStatus.Suspended)
            throw ErreurPilotage.Conflit("Seule une instance suspendue peut être reprise.");
        await client.ResumeInstanceAsync(etat.OrchestrationInstance, raison ?? "Reprise depuis le tableau de bord.");
    }

    /// <summary>
    /// Relance une instance en échec à partir de l'étape fautive : les activités échouées sont
    /// retirées de l'historique et rejouées (rewind), le reste est conservé.
    /// </summary>
    public async Task RelancerAsync(string instanceId, string? raison)
    {
        var etat = await EtatAsync(instanceId);
        if (etat.OrchestrationStatus != OrchestrationStatus.Failed)
            throw ErreurPilotage.Conflit("Seule une instance en échec peut être relancée.");
        await service.RewindTaskOrchestrationAsync(instanceId, raison ?? "Relancée depuis le tableau de bord.");
    }

    /// <summary>Démarre une nouvelle instance avec les mêmes entrées.</summary>
    public async Task<ResultatDemarrage> RedemarrerAsync(string instanceId, bool versionCourante, string? par)
    {
        var etat = await EtatAsync(instanceId);
        var entree = EntreeOrchestration.Lire(etat.Input);
        return await DemarrerAsync(entree.DefinitionId, entree.Entrees, versionCourante ? null : entree.Version, null, par);
    }

    public async Task PurgerAsync(string instanceId)
    {
        var etat = await EtatAsync(instanceId);
        if (StatutsActifs.Contains(etat.OrchestrationStatus))
            throw ErreurPilotage.Conflit("Une instance active ne peut être supprimée : interrompez-la d'abord.");
        await service.PurgeInstanceStateAsync(instanceId);
    }

    public async Task<InstanceDetail> ObtenirAsync(string instanceId)
    {
        var e = await EtatAsync(instanceId);
        var sortie = Json.Lire(e.Output);
        ErreurInstance? erreur = e.FailureDetails is { } f
            ? new ErreurInstance(f.ErrorType, f.ErrorMessage, f.StackTrace)
            : e.OrchestrationStatus == OrchestrationStatus.Terminated
                ? new ErreurInstance("Interruption", Expressions.Expression.EnTexte(sortie), null)
                : null;

        return new InstanceDetail(
            e.OrchestrationInstance.InstanceId,
            e.Name,
            e.Version,
            e.OrchestrationStatus.ToString(),
            Utc(e.CreatedTime),
            Utc(e.LastUpdatedTime),
            e.CompletedTime.Year > 2000 && e.OrchestrationStatus is not (OrchestrationStatus.Running or OrchestrationStatus.Pending or OrchestrationStatus.Suspended)
                ? Utc(e.CompletedTime) : null,
            e.ParentInstance?.OrchestrationInstance?.InstanceId,
            (Json.Lire(e.Input) as JsonObject)?["entrees"]?.DeepClone(),
            erreur is null ? sortie : null,
            Json.Lire(e.Status),
            erreur,
            e.Tags);
    }

    /// <summary>
    /// Attend, au plus <paramref name="delai"/>, qu'une étape « reponse » publie sa réponse ou que
    /// l'instance se termine. Le statut personnalisé est persisté à chaque point de contrôle de
    /// l'orchestration : une réponse suivie d'une attente (événement, activité) est donc visible
    /// immédiatement, pendant que le processus continue.
    /// </summary>
    public async Task<ResultatAttente> AttendreReponseAsync(string instanceId, TimeSpan delai, CancellationToken ct = default)
    {
        var limite = DateTime.UtcNow + delai;
        var pause = TimeSpan.FromMilliseconds(100);
        while (true)
        {
            var etat = await client.GetOrchestrationStateAsync(instanceId);
            if (etat is not null)
            {
                var statut = Json.Lire(etat.Status) as JsonObject;
                if (statut?["reponse"] is JsonObject reponse)
                    return new ResultatAttente(instanceId, etat.OrchestrationStatus.ToString(), NatureAttente.Reponse,
                        reponse["statutHttp"]?.GetValue<int>() ?? 200, reponse["corps"]?.DeepClone(), statut);

                if (etat.OrchestrationStatus is OrchestrationStatus.Completed)
                    return new ResultatAttente(instanceId, etat.OrchestrationStatus.ToString(), NatureAttente.Terminee, 200, Json.Lire(etat.Output), statut);

                if (etat.OrchestrationStatus is OrchestrationStatus.Failed or OrchestrationStatus.Terminated or OrchestrationStatus.Canceled)
                    return new ResultatAttente(instanceId, etat.OrchestrationStatus.ToString(), NatureAttente.Echec, 500,
                        JsonValue.Create(etat.FailureDetails?.ErrorMessage ?? Expressions.Expression.EnTexte(Json.Lire(etat.Output))), statut);
            }

            var restant = limite - DateTime.UtcNow;
            if (restant <= TimeSpan.Zero)
                return new ResultatAttente(instanceId, etat?.OrchestrationStatus.ToString() ?? "Pending", NatureAttente.DelaiDepasse, 202, null,
                    Json.Lire(etat?.Status) as JsonObject);

            await Task.Delay(restant < pause ? restant : pause, ct);
            pause = TimeSpan.FromMilliseconds(Math.Min(pause.TotalMilliseconds * 1.5, 1000));
        }
    }

    private async Task<OrchestrationState> EtatAsync(string instanceId) =>
        await client.GetOrchestrationStateAsync(instanceId)
        ?? throw ErreurPilotage.Introuvable($"Instance « {instanceId} » introuvable.");

    private async Task<OrchestrationState> EtatActifAsync(string instanceId)
    {
        var etat = await EtatAsync(instanceId);
        if (!StatutsActifs.Contains(etat.OrchestrationStatus))
            throw ErreurPilotage.Conflit($"L'instance « {instanceId} » n'est plus active ({etat.OrchestrationStatus}).");
        return etat;
    }

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
}
