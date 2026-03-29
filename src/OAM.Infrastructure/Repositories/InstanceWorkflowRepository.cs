using Microsoft.EntityFrameworkCore;
using OAM.Domain.Entities;
using OAM.Domain.Enums;
using OAM.Domain.Interfaces;
using OAM.Infrastructure.Data;

namespace OAM.Infrastructure.Repositories;

public class InstanceWorkflowRepository(OamDbContext db) : IInstanceWorkflowRepository
{
    public async Task<InstanceWorkflow?> ObtenirParIdAsync(Guid id) =>
        await db.InstancesWorkflow
            .Include(i => i.Taches.OrderBy(t => t.Ordre))
            .Include(i => i.DefinitionWorkflow)
            .FirstOrDefaultAsync(i => i.Id == id);

    public async Task<IReadOnlyList<InstanceWorkflow>> ListerAsync(EtatWorkflow? etat = null, Guid? definitionId = null)
    {
        var query = db.InstancesWorkflow
            .Include(i => i.DefinitionWorkflow)
            .AsQueryable();

        if (etat.HasValue)
            query = query.Where(i => i.Etat == etat.Value);
        if (definitionId.HasValue)
            query = query.Where(i => i.DefinitionWorkflowId == definitionId.Value);

        return await query.OrderByDescending(i => i.DateCreation).ToListAsync();
    }

    public async Task<InstanceWorkflow> CreerAsync(InstanceWorkflow instance)
    {
        db.InstancesWorkflow.Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }

    public async Task MettreAJourAsync(InstanceWorkflow instance)
    {
        db.InstancesWorkflow.Update(instance);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<InstanceWorkflow>> ListerEnErreurAsync(Guid? definitionId = null)
    {
        var query = db.InstancesWorkflow
            .Include(i => i.Taches.Where(t => t.Etat == EtatTache.EnErreur))
            .Include(i => i.DefinitionWorkflow)
            .Where(i => i.Etat == EtatWorkflow.EnErreur);

        if (definitionId.HasValue)
            query = query.Where(i => i.DefinitionWorkflowId == definitionId.Value);

        return await query.OrderByDescending(i => i.DateCreation).ToListAsync();
    }
}
