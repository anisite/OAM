using Microsoft.EntityFrameworkCore;
using OAM.Domain.Entities;
using OAM.Domain.Interfaces;
using OAM.Infrastructure.Data;

namespace OAM.Infrastructure.Repositories;

public class ExecutionTacheRepository(OamDbContext db) : IExecutionTacheRepository
{
    public async Task<ExecutionTache?> ObtenirParIdAsync(Guid id) =>
        await db.ExecutionsTache.FirstOrDefaultAsync(t => t.Id == id);

    public async Task<IReadOnlyList<ExecutionTache>> ListerParInstanceAsync(Guid instanceWorkflowId) =>
        await db.ExecutionsTache
            .Where(t => t.InstanceWorkflowId == instanceWorkflowId)
            .OrderBy(t => t.Ordre)
            .ToListAsync();

    public async Task<ExecutionTache> CreerAsync(ExecutionTache tache)
    {
        db.ExecutionsTache.Add(tache);
        await db.SaveChangesAsync();
        return tache;
    }

    public async Task MettreAJourAsync(ExecutionTache tache)
    {
        db.ExecutionsTache.Update(tache);
        await db.SaveChangesAsync();
    }

    public async Task MettreAJourEnLotAsync(IEnumerable<ExecutionTache> taches)
    {
        db.ExecutionsTache.UpdateRange(taches);
        await db.SaveChangesAsync();
    }
}
