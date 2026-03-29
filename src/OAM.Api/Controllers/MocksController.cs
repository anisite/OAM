using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OAM.Workflow.Core.Engine;

namespace OAM.Api.Controllers;

/// <summary>
/// Gestion du catalogue de mocks pour les tests.
/// </summary>
[ApiController]
[Route("api/mocks")]
[Authorize]
public class MocksController(GestionnaireMock gestionnaire) : ControllerBase
{
    [HttpPost]
    public IActionResult AjouterMock([FromBody] AjouterMockDto dto)
    {
        gestionnaire.AjouterMock(dto.Id, dto.Condition, dto.ReponseJson);
        return Ok();
    }

    [HttpPost("batch")]
    public IActionResult AjouterMocks([FromBody] List<AjouterMockDto> dtos)
    {
        foreach (var dto in dtos)
            gestionnaire.AjouterMock(dto.Id, dto.Condition, dto.ReponseJson);
        return Ok(new { ajoutes = dtos.Count });
    }
}

public record AjouterMockDto(string Id, string? Condition, string ReponseJson);
