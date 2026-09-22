using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OAM.Domain.Interfaces;

namespace OAM.Workflow.Core.Engine;

/// <summary>
/// Service hébergé qui traite les workflows en arrière-plan.
/// Chaque workflow s'exécute dans son propre scope DI, en parallèle.
/// </summary>
public class WorkflowBackgroundService(
    WorkflowQueue queue,
    IServiceProvider services,
    ILogger<WorkflowBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var instanceId in queue.LireAsync(ct))
        {
            // Fire-and-forget par design : chaque workflow tourne en parallèle
            _ = TraiterAsync(instanceId, ct);
        }
    }

    private async Task TraiterAsync(Guid instanceId, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var moteur = scope.ServiceProvider.GetRequiredService<IMoteurWorkflow>();
        try
        {
            await moteur.ExecuterAsync(instanceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur non gérée lors du traitement de l'instance {InstanceId}", instanceId);
        }
    }
}
