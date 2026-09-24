using DurableTask.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OIM.Moteur.Orchestration;
using OIM.Moteur.Orchestration.Activites;

namespace OIM.Moteur.Hebergement;

/// <summary>
/// Toute orchestration, quel que soit son nom (= id du processus), est servie par l'interpréteur.
/// Déployer un nouveau YAML ne demande donc aucun redémarrage du worker.
/// </summary>
public sealed class FabriqueOrchestrations(ILoggerFactory journaux) : INameVersionObjectManager<TaskOrchestration>
{
    public void Add(ObjectCreator<TaskOrchestration> creator) =>
        throw new NotSupportedException("Les orchestrations sont interprétées à partir des définitions YAML.");

    public TaskOrchestration GetObject(string name, string? version) =>
        new OrchestrationProcessus(journaux.CreateLogger<OrchestrationProcessus>());
}

public sealed class FabriqueActivites(IServiceProvider services) : INameVersionObjectManager<TaskActivity>
{
    private static readonly Dictionary<string, Type> Types = new()
    {
        [NomsActivites.ChargerDefinition] = typeof(ChargerDefinitionActivite),
        [NomsActivites.Http] = typeof(HttpActivite),
        [NomsActivites.Courriel] = typeof(CourrielActivite),
        [NomsActivites.Reponse] = typeof(ReponseActivite),
        [NomsActivites.Service] = typeof(ServiceActivite),
        [NomsActivites.ResoudreBoite] = typeof(ResoudreBoiteActivite)
    };

    public void Add(ObjectCreator<TaskActivity> creator) =>
        throw new NotSupportedException("Les activités sont enregistrées dans le conteneur d'injection.");

    public TaskActivity GetObject(string name, string? version) =>
        Types.TryGetValue(name, out var type) ? (TaskActivity)services.GetRequiredService(type) : null!;
}
