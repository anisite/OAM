using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OAM.Api.Dtos;
using OAM.Domain.Entities;
using OAM.Domain.Interfaces;
using OAM.Workflow.Core.Yaml;

namespace OAM.Api.Controllers;

[ApiController]
[Route("api/definitions")]
[Authorize]
public class DefinitionsController(IDefinitionWorkflowRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DefinitionWorkflowDto>>> Lister([FromQuery] string? equipe)
    {
        var definitions = await repo.ListerAsync(equipe);
        return Ok(definitions.Select(d => new DefinitionWorkflowDto(
            d.Id, d.Nom, d.Description, d.Equipe, d.HashVersion,
            d.DateCreation, d.DateModification, d.Actif)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DefinitionWorkflowDto>> ObtenirParId(Guid id)
    {
        var def = await repo.ObtenirParIdAsync(id);
        if (def is null) return NotFound();
        return Ok(new DefinitionWorkflowDto(
            def.Id, def.Nom, def.Description, def.Equipe, def.HashVersion,
            def.DateCreation, def.DateModification, def.Actif));
    }

    [HttpGet("{id:guid}/yaml")]
    public async Task<ActionResult<string>> ObtenirYaml(Guid id)
    {
        var def = await repo.ObtenirParIdAsync(id);
        if (def is null) return NotFound();
        return Ok(def.ContenuYaml);
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<IReadOnlyList<VersionDefinitionDto>>> ListerVersions(Guid id)
    {
        var versions = await repo.ListerVersionsAsync(id);
        return Ok(versions.Select(v => new VersionDefinitionDto(
            v.Id, v.HashVersion, v.DateChargement, v.DeployePar)));
    }

    /// <summary>
    /// Endpoint de déploiement de workflow par les équipes.
    /// Calcule automatiquement le hash de version.
    /// </summary>
    [HttpPost("deployer")]
    public async Task<ActionResult<DefinitionWorkflowDto>> Deployer([FromBody] DeploiementDefinitionDto dto)
    {
        // Valider le YAML
        try
        {
            YamlParser.ParseDefinition(dto.ContenuYaml);
        }
        catch (Exception ex)
        {
            return BadRequest(new { erreur = "YAML invalide", details = ex.Message });
        }

        var hash = YamlParser.CalculerHash(dto.ContenuYaml);
        var definition = new DefinitionWorkflow
        {
            Nom = dto.Nom,
            Description = dto.Description,
            Equipe = dto.Equipe,
            ContenuYaml = dto.ContenuYaml,
            HashVersion = hash
        };

        var resultat = await repo.CreerOuMettreAJourAsync(definition);
        return Ok(new DefinitionWorkflowDto(
            resultat.Id, resultat.Nom, resultat.Description, resultat.Equipe,
            resultat.HashVersion, resultat.DateCreation, resultat.DateModification, resultat.Actif));
    }
}
