using OAM.Domain.Entities;
using OAM.Domain.Interfaces;
using OAM.Workflow.Core.Yaml;

namespace OAM.Api.Features.Definitions;

public static class DefinitionsEndpoints
{
    public static IEndpointRouteBuilder MapDefinitions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/definitions").RequireAuthorization();

        group.MapGet("/", async (IDefinitionWorkflowRepository repo, string? equipe) =>
        {
            var liste = await repo.ListerAsync(equipe);
            return Results.Ok(liste.Select(ToDto));
        });

        group.MapGet("/{id:guid}", async (IDefinitionWorkflowRepository repo, Guid id) =>
        {
            var def = await repo.ObtenirParIdAsync(id);
            return def is null ? Results.NotFound() : Results.Ok(ToDto(def));
        });

        group.MapGet("/{id:guid}/yaml", async (IDefinitionWorkflowRepository repo, Guid id) =>
        {
            var def = await repo.ObtenirParIdAsync(id);
            return def is null ? Results.NotFound() : Results.Content(def.ContenuYaml, "text/plain; charset=utf-8");
        });

        group.MapGet("/{id:guid}/versions", async (IDefinitionWorkflowRepository repo, Guid id) =>
        {
            var versions = await repo.ListerVersionsAsync(id);
            return Results.Ok(versions.Select(v =>
                new VersionDefinitionDto(v.Id, v.HashVersion, v.DateChargement, v.DeployePar)));
        });

        group.MapPost("/deployer", async (IDefinitionWorkflowRepository repo, DeploiementDefinitionDto dto) =>
        {
            try { YamlParser.ParseDefinition(dto.ContenuYaml); }
            catch (Exception ex)
            {
                return Results.BadRequest(new { erreur = "YAML invalide", details = ex.Message });
            }

            var definition = new DefinitionWorkflow
            {
                Nom = dto.Nom,
                Description = dto.Description,
                Equipe = dto.Equipe,
                ContenuYaml = dto.ContenuYaml,
                HashVersion = YamlParser.CalculerHash(dto.ContenuYaml)
            };

            var resultat = await repo.CreerOuMettreAJourAsync(definition);
            return Results.Ok(ToDto(resultat));
        });

        return app;
    }

    private static DefinitionWorkflowDto ToDto(DefinitionWorkflow d) => new(
        d.Id, d.Nom, d.Description, d.Equipe, d.HashVersion,
        d.DateCreation, d.DateModification, d.Actif);
}
