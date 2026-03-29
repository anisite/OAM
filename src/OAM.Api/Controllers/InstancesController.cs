using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OAM.Api.Dtos;
using OAM.Domain.Enums;
using OAM.Domain.Interfaces;

namespace OAM.Api.Controllers;

[ApiController]
[Route("api/instances")]
[Authorize]
public class InstancesController(
    IInstanceWorkflowRepository instanceRepo,
    IExecutionTacheRepository tacheRepo,
    IMoteurWorkflow moteur) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InstanceWorkflowDto>>> Lister(
        [FromQuery] EtatWorkflow? etat,
        [FromQuery] Guid? definitionId)
    {
        var instances = await instanceRepo.ListerAsync(etat, definitionId);
        return Ok(instances.Select(MapperInstance));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InstanceWorkflowDto>> ObtenirParId(Guid id)
    {
        var instance = await instanceRepo.ObtenirParIdAsync(id);
        if (instance is null) return NotFound();
        return Ok(MapperInstance(instance));
    }

    [HttpGet("erreurs")]
    public async Task<ActionResult<IReadOnlyList<InstanceWorkflowDto>>> ListerEnErreur([FromQuery] Guid? definitionId)
    {
        var instances = await instanceRepo.ListerEnErreurAsync(definitionId);
        return Ok(instances.Select(MapperInstance));
    }

    [HttpPost("demarrer")]
    public async Task<ActionResult<InstanceWorkflowDto>> Demarrer([FromBody] DemarrerWorkflowDto dto)
    {
        var instance = await moteur.DemarrerAsync(dto.DefinitionId, dto.DonneesEntree, dto.CorrelationId);
        return Ok(MapperInstance(instance));
    }

    [HttpPost("{id:guid}/reprendre")]
    public async Task<IActionResult> Reprendre(Guid id)
    {
        await moteur.ReprendreAsync(id);
        return Ok();
    }

    [HttpPost("{id:guid}/reprendre-tache")]
    public async Task<IActionResult> ReprendreTache(Guid id, [FromBody] ReprendreTacheDto dto)
    {
        await moteur.ReprendreTacheAsync(id, dto.NomTache, dto.DonneesEntreeCorrigees);
        return Ok();
    }

    [HttpPost("{id:guid}/pauser")]
    public async Task<IActionResult> Pauser(Guid id)
    {
        await moteur.PauserAsync(id);
        return Ok();
    }

    [HttpPost("{id:guid}/annuler")]
    public async Task<IActionResult> Annuler(Guid id)
    {
        await moteur.AnnulerAsync(id);
        return Ok();
    }

    /// <summary>
    /// Correction en lot des tâches en erreur (PATCH).
    /// </summary>
    [HttpPatch("{id:guid}/taches")]
    public async Task<IActionResult> PatchTaches(Guid id, [FromBody] PatchTachesDto dto)
    {
        var instance = await instanceRepo.ObtenirParIdAsync(id);
        if (instance is null) return NotFound();

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
        return Ok();
    }

    /// <summary>
    /// Endpoint webhook : un système externe reprend une tâche hook.
    /// </summary>
    [HttpPost("{id:guid}/hook/{nomTache}")]
    [AllowAnonymous]
    public async Task<IActionResult> Hook(Guid id, string nomTache, [FromBody] object? donnees)
    {
        await moteur.ReprendreTacheAsync(id, nomTache, donnees?.ToString());
        return Ok();
    }

    private static InstanceWorkflowDto MapperInstance(Domain.Entities.InstanceWorkflow i) => new(
        i.Id,
        i.DefinitionWorkflowId,
        i.DefinitionWorkflow?.Nom ?? "",
        i.CorrelationId,
        i.HashVersionConfig,
        i.Etat,
        i.DateCreation,
        i.DateDebut,
        i.DateFin,
        i.Erreur,
        i.Taches.Select(t => new ExecutionTacheDto(
            t.Id, t.NomTache, t.TypeConnecteur, t.Ordre, t.Etat,
            t.DonneesEntree, t.DonneesSortie, t.Erreur,
            t.DateDebut, t.DateFin)).ToList()
    );
}
