using Microsoft.Extensions.Logging;
using OAM.Domain.Interfaces;

namespace OAM.Workflow.Core.Connecteurs;

/// <summary>
/// Connecteur mock pour les tests et l'environnement QA.
/// Résolution depuis un catalogue de réponses préconfigurées.
/// </summary>
public class MockConnecteur(
    IMockResolver mockResolver,
    ILogger<MockConnecteur> logger) : IConnecteur
{
    public string Type => "mock";

    public async Task<ResultatConnecteur> ExecuterAsync(ContexteConnecteur contexte)
    {
        var mockId = contexte.Parametres.GetValueOrDefault("mock")?.ToString();
        if (string.IsNullOrEmpty(mockId))
            return new ResultatConnecteur(false, null, "Aucun mock défini pour cette tâche");

        var resultat = await mockResolver.ResoudreAsync(mockId, contexte.Parametres);

        logger.LogInformation(
            "Mock {MockId} résolu pour tâche {NomTache} [CorrelationId={CorrelationId}]",
            mockId, contexte.NomTache, contexte.CorrelationId);

        return new ResultatConnecteur(true, resultat);
    }
}

public interface IMockResolver
{
    Task<object?> ResoudreAsync(string mockId, Dictionary<string, object?> parametres);
}
