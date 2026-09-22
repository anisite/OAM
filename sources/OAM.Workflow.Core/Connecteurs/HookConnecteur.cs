using Microsoft.Extensions.Logging;
using OAM.Domain.Interfaces;

namespace OAM.Workflow.Core.Connecteurs;

/// <summary>
/// Connecteur de type Hook — met le workflow en pause en attendant
/// un appel externe pour reprendre l'exécution.
/// </summary>
public class HookConnecteur(ILogger<HookConnecteur> logger) : IConnecteur
{
    public string Type => "hook";

    public Task<ResultatConnecteur> ExecuterAsync(ContexteConnecteur contexte)
    {
        logger.LogInformation(
            "Hook en attente pour tâche {NomTache} [CorrelationId={CorrelationId}]",
            contexte.NomTache, contexte.CorrelationId);

        return Task.FromResult(new ResultatConnecteur(true, new { enAttente = true, hook = contexte.NomTache }));
    }
}
