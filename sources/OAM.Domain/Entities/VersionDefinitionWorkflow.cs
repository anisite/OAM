namespace OAM.Domain.Entities;

/// <summary>
/// Historique des versions d'une définition de workflow.
/// Chaque version est identifiée par un hash (comme un commit git).
/// </summary>
public class VersionDefinitionWorkflow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DefinitionWorkflowId { get; set; }
    public required string HashVersion { get; set; }
    public required string ContenuYaml { get; set; }
    public string? ContenuExtensions { get; set; }
    public string? ContenuHttpClients { get; set; }
    public DateTime DateChargement { get; set; } = DateTime.UtcNow;
    public string? DeployePar { get; set; }

    public DefinitionWorkflow? DefinitionWorkflow { get; set; }
}
