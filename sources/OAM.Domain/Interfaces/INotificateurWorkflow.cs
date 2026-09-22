using OAM.Domain.Entities;
using OAM.Domain.Enums;

namespace OAM.Domain.Interfaces;

/// <summary>
/// Interface pour la diffusion des événements de workflow en temps réel (SignalR).
/// </summary>
public interface INotificateurWorkflow
{
    Task NotifierChangementEtatAsync(Guid instanceId, EtatWorkflow etat, string correlationId);
    Task NotifierTacheDemarreeAsync(Guid instanceId, ExecutionTache tache, string correlationId);
    Task NotifierTacheTermineeAsync(Guid instanceId, ExecutionTache tache, string correlationId);
    Task NotifierTacheEnErreurAsync(Guid instanceId, ExecutionTache tache, string correlationId);
    Task NotifierWorkflowTermineAsync(Guid instanceId, string correlationId);
}
