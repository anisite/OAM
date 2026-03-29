using Microsoft.EntityFrameworkCore;
using OAM.Domain.Entities;
using OAM.Domain.Interfaces;
using OAM.Infrastructure.Data;

namespace OAM.Infrastructure.Repositories;

public class DefinitionWorkflowRepository(OamDbContext db) : IDefinitionWorkflowRepository
{
    public async Task<DefinitionWorkflow?> ObtenirParIdAsync(Guid id) =>
        await db.DefinitionsWorkflow
            .Include(d => d.Versions.OrderByDescending(v => v.DateChargement).Take(5))
            .FirstOrDefaultAsync(d => d.Id == id);

    public async Task<DefinitionWorkflow?> ObtenirParNomAsync(string nom) =>
        await db.DefinitionsWorkflow.FirstOrDefaultAsync(d => d.Nom == nom);

    public async Task<IReadOnlyList<DefinitionWorkflow>> ListerAsync(string? equipe = null)
    {
        var query = db.DefinitionsWorkflow.Where(d => d.Actif);
        if (!string.IsNullOrEmpty(equipe))
            query = query.Where(d => d.Equipe == equipe);
        return await query.OrderBy(d => d.Nom).ToListAsync();
    }

    public async Task<DefinitionWorkflow> CreerOuMettreAJourAsync(DefinitionWorkflow definition)
    {
        var existante = await db.DefinitionsWorkflow.FirstOrDefaultAsync(d => d.Nom == definition.Nom);
        if (existante is not null)
        {
            existante.ContenuYaml = definition.ContenuYaml;
            existante.HashVersion = definition.HashVersion;
            existante.Description = definition.Description;
            existante.Equipe = definition.Equipe;
            existante.DateModification = DateTime.UtcNow;
        }
        else
        {
            db.DefinitionsWorkflow.Add(definition);
            existante = definition;
        }

        db.VersionsDefinitionWorkflow.Add(new VersionDefinitionWorkflow
        {
            DefinitionWorkflowId = existante.Id,
            HashVersion = existante.HashVersion,
            ContenuYaml = existante.ContenuYaml,
            DeployePar = definition.Equipe
        });

        await db.SaveChangesAsync();
        return existante;
    }

    public async Task<IReadOnlyList<VersionDefinitionWorkflow>> ListerVersionsAsync(Guid definitionId) =>
        await db.VersionsDefinitionWorkflow
            .Where(v => v.DefinitionWorkflowId == definitionId)
            .OrderByDescending(v => v.DateChargement)
            .ToListAsync();
}
