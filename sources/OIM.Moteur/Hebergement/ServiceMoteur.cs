using Dapper;
using DurableTask.Core;
using DurableTask.SqlServer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OIM.Moteur.Stockage;

namespace OIM.Moteur.Hebergement;

/// <summary>
/// Démarre le moteur : schéma DurableTask (dt) et OIM (oim), déploiement des définitions
/// du dossier configuré, puis le worker DurableTask qui exécute orchestrations et activités.
/// </summary>
public sealed class ServiceMoteur(
    SqlOrchestrationService service,
    TaskHubWorker worker,
    IDepotDefinitions depot,
    ConnexionSql connexion,
    DeploiementDossier deploiementDossier,
    IOptions<OptionsOim> options,
    ILogger<ServiceMoteur> journal) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await service.CreateIfNotExistsAsync();

        if (options.Value.TaskHubParApplication)
        {
            await using var cn = await connexion.OuvrirAsync(cancellationToken);
            await cn.ExecuteAsync($"EXEC [{options.Value.SchemaDurableTask}].SetGlobalSetting @Name = 'TaskHubMode', @Value = 0");
        }

        await depot.InitialiserAsync(cancellationToken);
        await deploiementDossier.DeployerAsync(cancellationToken);

        worker.ErrorPropagationMode = ErrorPropagationMode.UseFailureDetails;
        await worker.StartAsync();
        journal.LogInformation("Moteur OIM démarré (task hub « {TaskHub} »).", options.Value.TaskHub);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await worker.StopAsync(isForced: false);
        journal.LogInformation("Moteur OIM arrêté.");
    }
}
