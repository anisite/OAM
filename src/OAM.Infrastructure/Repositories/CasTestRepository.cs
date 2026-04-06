using Microsoft.EntityFrameworkCore;
using OAM.Domain.Entities;
using OAM.Domain.Interfaces;
using OAM.Infrastructure.Data;

namespace OAM.Infrastructure.Repositories;

public class CasTestRepository(OamDbContext db) : ICasTestRepository
{
    public async Task<IReadOnlyList<CasTest>> ListerParDefinitionAsync(Guid definitionId) =>
        await db.CasTests
            .Where(c => c.DefinitionWorkflowId == definitionId)
            .OrderBy(c => c.Nom)
            .ToListAsync();

    public async Task<CasTest?> ObtenirAsync(Guid definitionId, string nom) =>
        await db.CasTests.FirstOrDefaultAsync(c => c.DefinitionWorkflowId == definitionId && c.Nom == nom);

    public async Task CreerOuMettreAJourEnLotAsync(IList<CasTest> casTests)
    {
        foreach (var casTest in casTests)
        {
            var existant = await db.CasTests
                .FirstOrDefaultAsync(c => c.DefinitionWorkflowId == casTest.DefinitionWorkflowId && c.Nom == casTest.Nom);

            if (existant is not null)
            {
                existant.Contenu = casTest.Contenu;
                existant.DateChargement = casTest.DateChargement;
                existant.DeployePar = casTest.DeployePar;
            }
            else
            {
                db.CasTests.Add(casTest);
            }
        }

        await db.SaveChangesAsync();
    }
}
