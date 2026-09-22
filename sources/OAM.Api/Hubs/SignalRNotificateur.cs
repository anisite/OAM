using Microsoft.AspNetCore.SignalR;
using OAM.Domain.Entities;
using OAM.Domain.Enums;
using OAM.Domain.Interfaces;

namespace OAM.Api.Hubs;

/// <summary>
/// Implémentation SignalR de INotificateurWorkflow.
/// </summary>
public class SignalRNotificateur(IHubContext<WorkflowHub> hubContext) : INotificateurWorkflow
{
    public async Task NotifierChangementEtatAsync(Guid instanceId, EtatWorkflow etat, string correlationId)
    {
        var payload = new { instanceId, etat = etat.ToString(), correlationId, timestamp = DateTime.UtcNow };
        await hubContext.Clients.Group($"instance-{instanceId}").SendAsync("ChangementEtat", payload);
        await hubContext.Clients.Group($"correlation-{correlationId}").SendAsync("ChangementEtat", payload);
        await hubContext.Clients.All.SendAsync("ChangementEtatGlobal", payload);
    }

    public async Task NotifierTacheDemarreeAsync(Guid instanceId, ExecutionTache tache, string correlationId)
    {
        var payload = new { instanceId, tacheId = tache.Id, nomTache = tache.NomTache, correlationId, timestamp = DateTime.UtcNow };
        await hubContext.Clients.Group($"instance-{instanceId}").SendAsync("TacheDemarree", payload);
        await hubContext.Clients.Group($"correlation-{correlationId}").SendAsync("TacheDemarree", payload);
    }

    public async Task NotifierTacheTermineeAsync(Guid instanceId, ExecutionTache tache, string correlationId)
    {
        var payload = new
        {
            instanceId, tacheId = tache.Id, nomTache = tache.NomTache,
            donneesSortie = tache.DonneesSortie, correlationId, timestamp = DateTime.UtcNow
        };
        await hubContext.Clients.Group($"instance-{instanceId}").SendAsync("TacheTerminee", payload);
        await hubContext.Clients.Group($"correlation-{correlationId}").SendAsync("TacheTerminee", payload);
    }

    public async Task NotifierTacheEnErreurAsync(Guid instanceId, ExecutionTache tache, string correlationId)
    {
        var payload = new
        {
            instanceId, tacheId = tache.Id, nomTache = tache.NomTache,
            erreur = tache.Erreur, correlationId, timestamp = DateTime.UtcNow
        };
        await hubContext.Clients.Group($"instance-{instanceId}").SendAsync("TacheEnErreur", payload);
        await hubContext.Clients.Group($"correlation-{correlationId}").SendAsync("TacheEnErreur", payload);
        await hubContext.Clients.All.SendAsync("TacheEnErreurGlobal", payload);
    }

    public async Task NotifierWorkflowTermineAsync(Guid instanceId, string correlationId)
    {
        var payload = new { instanceId, correlationId, timestamp = DateTime.UtcNow };
        await hubContext.Clients.Group($"instance-{instanceId}").SendAsync("WorkflowTermine", payload);
        await hubContext.Clients.Group($"correlation-{correlationId}").SendAsync("WorkflowTermine", payload);
        await hubContext.Clients.All.SendAsync("WorkflowTermineGlobal", payload);
    }
}
