using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OAM.Domain.Interfaces;

namespace OAM.Workflow.Core.Engine;

/// <summary>
/// Détecte les instances orphelines (serveur crashé) et les re-enfile pour reprise.
/// Une instance est orpheline si EnCours sans heartbeat depuis > SeuilInactivite,
/// ou EnAttente depuis > SeuilInactivite (jamais traitée).
/// </summary>
public class OrphelinRecuperateur(
    WorkflowQueue queue,
    IServiceProvider services,
    ILogger<OrphelinRecuperateur> logger) : BackgroundService
{
    private static readonly TimeSpan SeuilInactivite = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan IntervalleVerification = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Attendre le démarrage complet de l'app avant la première vérification
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RecupererOrphelinsAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la récupération des orphelins");
            }

            await Task.Delay(IntervalleVerification, ct);
        }
    }

    private async Task RecupererOrphelinsAsync()
    {
        using var scope = services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IInstanceWorkflowRepository>();

        var orphelins = await repo.ListerOrphelinsAsync(SeuilInactivite);
        if (orphelins.Count == 0) return;

        logger.LogWarning("{Count} instance(s) orpheline(s) détectée(s), re-enfilage...", orphelins.Count);

        foreach (var instance in orphelins)
        {
            queue.Enfiler(instance.Id);
            logger.LogInformation("Instance {Id} [CorrelationId={CorrelationId}] re-enfilée",
                instance.Id, instance.CorrelationId);
        }
    }
}
