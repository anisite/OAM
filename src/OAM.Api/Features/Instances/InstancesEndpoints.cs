using OAM.Domain.Enums;
using OAM.Domain.Interfaces;

namespace OAM.Api.Features.Instances;

public static class InstancesEndpoints
{
    public static IEndpointRouteBuilder MapInstances(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/instances").RequireAuthorization();

        group.MapGet("/", async (IInstanceWorkflowRepository repo, EtatWorkflow? etat, Guid? definitionId) =>
            Results.Ok((await repo.ListerAsync(etat, definitionId)).Select(ToDto)));

        group.MapGet("/{id:guid}", async (IInstanceWorkflowRepository repo, Guid id) =>
        {
            var instance = await repo.ObtenirParIdAsync(id);
            return instance is null ? Results.NotFound() : Results.Ok(ToDto(instance));
        });

        group.MapGet("/erreurs", async (IInstanceWorkflowRepository repo, Guid? definitionId) =>
            Results.Ok((await repo.ListerEnErreurAsync(definitionId)).Select(ToDto)));

        group.MapPost("/demarrer", async (IMoteurWorkflow moteur, DemarrerWorkflowDto dto) =>
        {
            var instance = await moteur.DemarrerAsync(dto.DefinitionId, dto.DonneesEntree, dto.CorrelationId);
            return Results.Ok(ToDto(instance));
        });

        group.MapPost("/{id:guid}/reprendre", async (IMoteurWorkflow moteur, Guid id) =>
        {
            await moteur.ReprendreAsync(id);
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/reprendre-tache", async (IMoteurWorkflow moteur, Guid id, ReprendreTacheDto dto) =>
        {
            await moteur.ReprendreTacheAsync(id, dto.NomTache, dto.DonneesEntreeCorrigees);
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/pauser", async (IMoteurWorkflow moteur, Guid id) =>
        {
            await moteur.PauserAsync(id);
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/annuler", async (IMoteurWorkflow moteur, Guid id) =>
        {
            await moteur.AnnulerAsync(id);
            return Results.Ok();
        });

        group.MapPatch("/{id:guid}/taches", async (
            IInstanceWorkflowRepository instanceRepo,
            IExecutionTacheRepository tacheRepo,
            Guid id, PatchTachesDto dto) =>
        {
            var instance = await instanceRepo.ObtenirParIdAsync(id);
            if (instance is null) return Results.NotFound();

            foreach (var patch in dto.Taches)
            {
                var tache = instance.Taches.FirstOrDefault(t => t.Id == patch.TacheId);
                if (tache is null) continue;
                if (patch.DonneesEntreeCorrigees is not null)
                    tache.DonneesEntree = patch.DonneesEntreeCorrigees;
                tache.Etat = EtatTache.EnAttente;
                tache.Erreur = null;
            }

            await tacheRepo.MettreAJourEnLotAsync(instance.Taches.ToList());
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/hook/{nomTache}", async (IMoteurWorkflow moteur, Guid id, string nomTache, object? donnees) =>
        {
            await moteur.ReprendreTacheAsync(id, nomTache, donnees?.ToString());
            return Results.Ok();
        }).AllowAnonymous();

        return app;
    }

    internal static InstanceWorkflowDto ToDto(Domain.Entities.InstanceWorkflow i) => new(
        i.Id, i.DefinitionWorkflowId, i.DefinitionWorkflow?.Nom ?? "",
        i.CorrelationId, i.HashVersionConfig, i.Etat,
        i.DateCreation, i.DateDebut, i.DateFin, i.Erreur,
        i.Taches.Select(t => new ExecutionTacheDto(
            t.Id, t.NomTache, t.TypeConnecteur, t.Ordre, t.Etat,
            t.DonneesEntree, t.DonneesSortie, t.Erreur,
            t.DateDebut, t.DateFin)).ToList());
}
