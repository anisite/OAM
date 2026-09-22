using OAM.Domain.Entities;

namespace OAM.Domain.Interfaces;

public interface ICasTestRepository
{
    Task<IReadOnlyList<CasTest>> ListerParDefinitionAsync(Guid definitionId);
    Task<CasTest?> ObtenirAsync(Guid definitionId, string nom);
    Task CreerOuMettreAJourEnLotAsync(IList<CasTest> casTests);
}
