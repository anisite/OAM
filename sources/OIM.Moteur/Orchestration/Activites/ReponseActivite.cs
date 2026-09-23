using System.Text.Json.Nodes;
using DurableTask.Core;
using Microsoft.Extensions.Logging;

namespace OIM.Moteur.Orchestration.Activites;

/// <summary>
/// Étape <c>type: reponse</c> : trace dans l'historique DurableTask la réponse retournée à
/// l'appelant synchrone — planification : gabarit + contexte reçu; résultat : réponse produite.
/// La réponse elle-même est déjà publiée par le statut personnalisé (sans attendre cette activité) :
/// l'appelant ne subit aucune latence supplémentaire.
/// </summary>
public sealed class ReponseActivite(ILogger<ReponseActivite> journal) : ActiviteJson
{
    protected override Task<JsonNode?> ExecuterAsync(TaskContext contexte, JsonObject p)
    {
        // Instances démarrées avant l'ajout du contexte : les paramètres portent déjà la réponse évaluée.
        if (p["contexte"] is not JsonObject ctx)
            return Task.FromResult<JsonNode?>(new JsonObject { ["statutHttp"] = p["statutHttp"]?.DeepClone(), ["corps"] = p["corps"]?.DeepClone() });

        var (statutHttp, corps) = ReponseAppelant.Evaluer(p["statutHttp"], p["corps"], ctx);
        journal.LogInformation("[{Instance}] Réponse HTTP {Statut} publiée à l'appelant (étape {Etape}).",
            contexte.OrchestrationInstance?.InstanceId, statutHttp, p["etape"]);

        // Résultat = réponse réellement produite (le gabarit et le contexte sont dans la planification).
        return Task.FromResult<JsonNode?>(new JsonObject { ["statutHttp"] = statutHttp, ["corps"] = corps });
    }
}
