using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        services.AddSingleton<ReponseConnecteur>();

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
            registre.Enregistrer(sp.GetRequiredService<ReponseConnecteur>());
            return registre;
        });

        // Réponse synchrone (swap AttenteReponseMemoire → AttenteReponseGarnet pour multi-serveurs)
        services.AddSingleton<IAttenteReponse, AttenteReponseMemoire>();

        // File d'attente + traitement en arrière-plan + récupération orphelins
        services.AddSingleton<WorkflowQueue>();
        services.AddHostedService<WorkflowBackgroundService>();
        services.AddHostedService<OrphelinRecuperateur>();

        // Moteur
        services.AddScoped<IMoteurWorkflow, MoteurWorkflow>();

        return services;
    }
}
