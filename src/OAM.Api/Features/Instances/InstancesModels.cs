using OAM.Domain.Enums;

namespace OAM.Api.Features.Instances;

public record InstanceWorkflowDto(
    Guid Id, Guid DefinitionWorkflowId, string NomWorkflow,
    string CorrelationId, string HashVersionConfig, EtatWorkflow Etat,
    DateTime DateCreation, DateTime? DateDebut, DateTime? DateFin,
    string? Erreur, List<ExecutionTacheDto> Taches);

public record ExecutionTacheDto(
    Guid Id, string NomTache, string TypeConnecteur, int Ordre, EtatTache Etat,
    string? DonneesEntree, string? DonneesSortie, string? Erreur,
    DateTime? DateDebut, DateTime? DateFin);

public record DemarrerWorkflowDto(
    Guid DefinitionId, string? DonneesEntree, string? CorrelationId);

public record ReprendreTacheDto(
    string NomTache, string? DonneesEntreeCorrigees);

public record PatchTachesDto(List<PatchTacheItem> Taches);

public record PatchTacheItem(Guid TacheId, string? DonneesEntreeCorrigees);
