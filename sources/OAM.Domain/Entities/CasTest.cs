namespace OAM.Domain.Entities;

/// <summary>
/// Cas de test associé à une définition de workflow.
/// Déployé indépendamment sans changer la version du workflow.
/// </summary>
public class CasTest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DefinitionWorkflowId { get; set; }
    public required string Nom { get; set; }
    public required string Contenu { get; set; }
    public DateTime DateChargement { get; set; } = DateTime.UtcNow;
    public string? DeployePar { get; set; }

    public DefinitionWorkflow DefinitionWorkflow { get; set; } = null!;
}
