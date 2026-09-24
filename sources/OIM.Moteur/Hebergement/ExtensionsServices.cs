using DurableTask.Core;
using DurableTask.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OIM.Moteur.Orchestration.Activites;
using OIM.Moteur.Pilotage;
using OIM.Moteur.Stockage;

namespace OIM.Moteur.Hebergement;

public static class ExtensionsServices
{
    /// <summary>
    /// Enregistre le moteur OIM : fournisseur DurableTask SQL Server, worker, client,
    /// dépôt des définitions et services de pilotage.
    /// </summary>
    public static IServiceCollection AjouterOim(this IServiceCollection services, IConfiguration configuration, bool demarrerWorker = true)
    {
        services.Configure<OptionsOim>(configuration.GetSection(OptionsOim.Section));

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<OptionsOim>>().Value;
            var chaine = o.ConnexionSql ?? configuration.GetConnectionString("Oim")
                         ?? throw new InvalidOperationException("Chaîne de connexion absente : Oim:ConnexionSql ou ConnectionStrings:Oim.");
            return new SqlOrchestrationServiceSettings(chaine, o.TaskHub, o.SchemaDurableTask)
            {
                CreateDatabaseIfNotExists = o.CreerBaseSiAbsente,
                MaxConcurrentActivities = o.MaxActivitesConcurrentes,
                MaxActiveOrchestrations = o.MaxOrchestrationsActives,
                MaxOrchestrationPollingInterval = o.ScrutationMax,
                MaxActivityPollingInterval = o.ScrutationMax,
                LoggerFactory = sp.GetRequiredService<ILoggerFactory>()
            };
        });
        services.AddSingleton(sp => new SqlOrchestrationService(sp.GetRequiredService<SqlOrchestrationServiceSettings>()));
        services.AddSingleton(sp => new ConnexionSql(sp.GetRequiredService<SqlOrchestrationServiceSettings>().TaskHubConnectionString));

        services.AddSingleton(sp => new TaskHubClient(
            sp.GetRequiredService<SqlOrchestrationService>(), null, sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton(sp => new TaskHubWorker(
            sp.GetRequiredService<SqlOrchestrationService>(),
            new FabriqueOrchestrations(sp.GetRequiredService<ILoggerFactory>()),
            new FabriqueActivites(sp),
            sp.GetRequiredService<ILoggerFactory>()));

        services.AddSingleton<IDepotDefinitions, DepotDefinitionsSql>();
        services.AddSingleton<DepotEquipesSql>();
        services.AddSingleton<ServiceEquipes>();
        services.AddTransient<ChargerDefinitionActivite>();
        services.AddTransient<HttpActivite>();
        services.AddTransient<CourrielActivite>();
        services.AddTransient<ReponseActivite>();

        services.AddSingleton<ServiceDefinitions>();
        services.AddSingleton<ServiceInstances>();
        services.AddSingleton<RequetesSuivi>();
        services.AddSingleton<DeploiementDossier>();
        services.AddSingleton<Tests.ServiceTests>();

        if (demarrerWorker)
            services.AddHostedService<ServiceMoteur>();

        return services;
    }
}
