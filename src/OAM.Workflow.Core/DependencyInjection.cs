using Microsoft.Extensions.DependencyInjection;
using OAM.Domain.Interfaces;
using OAM.Workflow.Core.Connecteurs;
using OAM.Workflow.Core.Engine;

namespace OAM.Workflow.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddOamWorkflowCore(this IServiceCollection services)
    {
        // Configuration HTTP
        services.AddSingleton<ConfigurationYamlHttp>();

        // Connecteurs
        services.AddSingleton<HttpConnecteur>();
        services.AddSingleton<MockConnecteur>();
        services.AddSingleton<ConditionConnecteur>();
        services.AddSingleton<HookConnecteur>();

        // Mock resolver
        services.AddSingleton<GestionnaireMock>();
        services.AddSingleton<IMockResolver>(sp => sp.GetRequiredService<GestionnaireMock>());

        // Registre des connecteurs
        services.AddSingleton<RegistreConnecteurs>(sp =>
        {
            var registre = new RegistreConnecteurs();
            registre.Enregistrer(sp.GetRequiredService<HttpConnecteur>());
            registre.Enregistrer(sp.GetRequiredService<MockConnecteur>());
            registre.Enregistrer(sp.GetRequiredService<ConditionConnecteur>());
            registre.Enregistrer(sp.GetRequiredService<HookConnecteur>());
            return registre;
        });

        // Moteur
        services.AddScoped<IMoteurWorkflow, MoteurWorkflow>();

        return services;
    }
}
