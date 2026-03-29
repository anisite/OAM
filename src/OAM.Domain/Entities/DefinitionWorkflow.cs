namespace OAM.Domain.Entities;

/// <summary>
/// Le plan abstrait d'un workflow : structure, étapes, règles de transition.
/// Aucune donnée d'exécution. Versionné par hash de configuration.
/// </summary>
public class DefinitionWorkflow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Nom { get; set; }
    public string? Description { get; set; }
    public required string ContenuYaml { get; set; }
    public required string HashVersion { get; set; }
    public string? Equipe { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
    public DateTime DateModification { get; set; } = DateTime.UtcNow;
    public bool Actif { get; set; } = true;

    public ICollection<InstanceWorkflow> Instances { get; set; } = [];
    public ICollection<VersionDefinitionWorkflow> Versions { get; set; } = [];
}
