using OAM.Domain.Entities;

namespace OAM.Domain.Interfaces;

public interface IMoteurWorkflow
{
    Task<InstanceWorkflow> DemarrerAsync(Guid definitionId, string? donneesEntree = null, string? correlationId = null);
    Task ReprendreAsync(Guid instanceId);
    Task ReprendreTacheAsync(Guid instanceId, string nomTache, string? donneesEntreeCorrigees = null);
    Task PauserAsync(Guid instanceId);
    Task AnnulerAsync(Guid instanceId);
}
