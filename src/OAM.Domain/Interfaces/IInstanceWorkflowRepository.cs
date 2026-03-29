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
}
