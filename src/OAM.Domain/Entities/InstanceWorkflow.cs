using OAM.Domain.Enums;

namespace OAM.Domain.Entities;

/// <summary>
/// Une exécution spécifique d'une définition de workflow.
/// Possède un état, un identifiant unique et des données de contexte.
/// </summary>
public class InstanceWorkflow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DefinitionWorkflowId { get; set; }
    public required string CorrelationId { get; set; }
    public required string HashVersionConfig { get; set; }
    public EtatWorkflow Etat { get; set; } = EtatWorkflow.EnAttente;
    public string? DonneesEntree { get; set; }
    public string? ContexteExecution { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
    public DateTime? DateDebut { get; set; }
    public DateTime? DateFin { get; set; }
    public string? Erreur { get; set; }

    public DefinitionWorkflow? DefinitionWorkflow { get; set; }
    public ICollection<ExecutionTache> Taches { get; set; } = [];
}
