using OAM.Domain.Entities;
using OAM.Domain.Enums;

namespace OAM.Domain.Interfaces;

public interface IInstanceWorkflowRepository
{
    Task<InstanceWorkflow?> ObtenirParIdAsync(Guid id);
    Task<IReadOnlyList<InstanceWorkflow>> ListerAsync(EtatWorkflow? etat = null, Guid? definitionId = null);
    Task<InstanceWorkflow> CreerAsync(InstanceWorkflow instance);
    Task MettreAJourAsync(InstanceWorkflow instance);
    Task<IReadOnlyList<InstanceWorkflow>> ListerEnErreurAsync(Guid? definitionId = null);
    Task<IReadOnlyList<InstanceWorkflow>> ListerOrphelinsAsync(TimeSpan seuilInactivite);
    Task MettreAJourHeartbeatAsync(Guid instanceId);
    /// <summary>
    /// Claim atomique : retourne true si ce serveur a obtenu le verrou exclusif sur l'instance.
    /// Utilise un UPDATE conditionnel — si deux serveurs tentent simultanément, un seul réussit.
    /// </summary>
    Task<bool> ReclamerAsync(Guid instanceId);
}
