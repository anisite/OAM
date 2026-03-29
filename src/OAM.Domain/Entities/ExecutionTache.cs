using OAM.Domain.Enums;

namespace OAM.Domain.Entities;

/// <summary>
/// Un bloc unitaire d'exécution au sein d'une instance de workflow.
/// Contient les données INPUT/OUTPUT modifiables en cas d'erreur.
/// </summary>
public class ExecutionTache
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstanceWorkflowId { get; set; }
    public required string NomTache { get; set; }
    public required string TypeConnecteur { get; set; }
    public int Ordre { get; set; }
    public EtatTache Etat { get; set; } = EtatTache.EnAttente;
    public string? DonneesEntree { get; set; }
    public string? DonneesSortie { get; set; }
    public string? Erreur { get; set; }
    public DateTime? DateDebut { get; set; }
    public DateTime? DateFin { get; set; }
    public int NombreTentatives { get; set; }

    public InstanceWorkflow? InstanceWorkflow { get; set; }
}
