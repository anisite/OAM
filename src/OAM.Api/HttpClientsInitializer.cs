using OAM.Domain.Interfaces;
using OAM.Workflow.Core.Connecteurs;

namespace OAM.Api;

/// <summary>
/// Au démarrage, recharge en mémoire les http-clients de chaque workflow actif depuis la BD.
/// Complète le fichier global http-clients.yml déjà chargé dans Program.cs.
/// </summary>
public class HttpClientsInitializer(
    IServiceProvider services,
    ConfigurationYamlHttp configHttp,
    ILogger<HttpClientsInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDefinitionWorkflowRepository>();

        var definitions = await repo.ListerAsync();
        var count = 0;

        foreach (var def in definitions)
        {
            if (def.ContenuHttpClients is null) continue;
            configHttp.ChargerDepuisYaml(def.ContenuHttpClients, def.Nom);
            count++;
        }

        if (count > 0)
            logger.LogInformation("Http-clients rechargés pour {Count} workflow(s) depuis la BD", count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
