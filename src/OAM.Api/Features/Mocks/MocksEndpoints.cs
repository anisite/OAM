using OAM.Workflow.Core.Engine;

namespace OAM.Api.Features.Mocks;

public static class MocksEndpoints
{
    public static IEndpointRouteBuilder MapMocks(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/mocks").RequireAuthorization();

        group.MapPost("/", (GestionnaireMock gestionnaire, AjouterMockDto dto) =>
        {
            gestionnaire.AjouterMock(dto.Id, GestionnaireMock.Global, dto.Condition, dto.ReponseJson);
            return Results.Ok();
        });

        group.MapPost("/batch", (GestionnaireMock gestionnaire, List<AjouterMockDto> dtos) =>
        {
            foreach (var dto in dtos)
                gestionnaire.AjouterMock(dto.Id, GestionnaireMock.Global, dto.Condition, dto.ReponseJson);
            return Results.Ok(new { ajoutes = dtos.Count });
        });

        group.MapDelete("/{id}", (GestionnaireMock gestionnaire, string id) =>
        {
            gestionnaire.RetirerMock(id, GestionnaireMock.Global);
            return Results.NoContent();
        });

        group.MapDelete("/", (GestionnaireMock gestionnaire) =>
        {
            gestionnaire.ViderMocks(GestionnaireMock.Global);
            return Results.NoContent();
        });

        return app;
    }
}

public record AjouterMockDto(string Id, string? Condition, string ReponseJson);
