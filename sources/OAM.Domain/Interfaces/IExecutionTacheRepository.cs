using OAM.Domain.Entities;

namespace OAM.Domain.Interfaces;

public interface IExecutionTacheRepository
{
    Task<ExecutionTache?> ObtenirParIdAsync(Guid id);
    Task<IReadOnlyList<ExecutionTache>> ListerParInstanceAsync(Guid instanceWorkflowId);
    Task<ExecutionTache> CreerAsync(ExecutionTache tache);
    Task MettreAJourAsync(ExecutionTache tache);
    Task MettreAJourEnLotAsync(IEnumerable<ExecutionTache> taches);
}
