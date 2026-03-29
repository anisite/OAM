namespace OAM.Api.Dtos;

public record DefinitionWorkflowDto(
    Guid Id,
    string Nom,
    string? Description,
    string? Equipe,
    string HashVersion,
    DateTime DateCreation,
    DateTime DateModification,
    bool Actif
);

public record VersionDefinitionDto(
    Guid Id,
    string HashVersion,
    DateTime DateChargement,
    string? DeployePar
);

public record DeploiementDefinitionDto(
    string Nom,
    string? Description,
    string? Equipe,
    string ContenuYaml
);
