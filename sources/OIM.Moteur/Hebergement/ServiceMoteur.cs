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
            await AppliquerTaskHubParApplicationAsync(cancellationToken);

        await depot.InitialiserAsync(cancellationToken);
        await deploiementDossier.DeployerAsync(cancellationToken);

        worker.ErrorPropagationMode = ErrorPropagationMode.UseFailureDetails;
        await worker.StartAsync();
        journal.LogInformation("Moteur OIM démarré (task hub « {TaskHub} »).", options.Value.TaskHub);
    }

    /// <summary>
    /// SetGlobalSetting est réservé aux administrateurs (absent du rôle dt_runtime) : on ne l'appelle
    /// que si le mode n'est pas déjà appliqué, ce qui permet à un DBA de créer la base
    /// (base-de-donnees/oim-creation.sql) et à l'application de tourner avec un compte à droits minimaux.
    /// </summary>
    private async Task AppliquerTaskHubParApplicationAsync(CancellationToken ct)
    {
        var dt = $"[{options.Value.SchemaDurableTask}]";
        await using var cn = await connexion.OuvrirAsync(ct);
        if (await cn.ExecuteScalarAsync<int>($"SELECT CASE WHEN {dt}.CurrentTaskHub() = APP_NAME() THEN 1 ELSE 0 END") == 1)
            return;

        try
        {
            await cn.ExecuteAsync($"EXEC {dt}.SetGlobalSetting @Name = 'TaskHubMode', @Value = 0");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 229)
        {
            throw new InvalidOperationException(
                "Le mode de task hub « par application » n'est pas appliqué et le compte SQL n'a pas le droit de le faire : " +
                $"un DBA doit exécuter EXEC {dt}.SetGlobalSetting @Name = 'TaskHubMode', @Value = 0 (voir base-de-donnees/oim-creation.sql).", ex);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await worker.StopAsync(isForced: false);
        journal.LogInformation("Moteur OIM arrêté.");
    }
}
