using Microsoft.EntityFrameworkCore;
using OIM.Domain.Entities;
using OIM.Domain.Interfaces;
using OIM.Infrastructure.Data;

namespace OIM.Infrastructure.Repositories;

public class ExecutionTacheRepository(OimDbContext db) : IExecutionTacheRepository
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
