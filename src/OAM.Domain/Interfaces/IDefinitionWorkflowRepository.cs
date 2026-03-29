using OAM.Domain.Entities;

namespace OAM.Domain.Interfaces;

public interface IDefinitionWorkflowRepository
{
    Task<DefinitionWorkflow?> ObtenirParIdAsync(Guid id);
    Task<DefinitionWorkflow?> ObtenirParNomAsync(string nom);
    Task<IReadOnlyList<DefinitionWorkflow>> ListerAsync(string? equipe = null);
    Task<DefinitionWorkflow> CreerOuMettreAJourAsync(DefinitionWorkflow definition);
    Task<IReadOnlyList<VersionDefinitionWorkflow>> ListerVersionsAsync(Guid definitionId);
}
