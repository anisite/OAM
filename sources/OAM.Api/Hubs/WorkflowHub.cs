using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace OAM.Api.Hubs;

/// <summary>
/// Hub SignalR dédié au suivi en temps réel des workflows.
/// Diffusion des événements : étape démarrée, terminée, erreur, workflow terminé.
/// Groupes par CorrelationId ou par utilisateur/rôle.
/// </summary>
[Authorize]
public class WorkflowHub : Hub
{
    public async Task RejoindreInstance(string instanceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"instance-{instanceId}");
    }

    public async Task QuitterInstance(string instanceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"instance-{instanceId}");
    }

    public async Task RejoindreCorrelation(string correlationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"correlation-{correlationId}");
    }

    public async Task QuitterCorrelation(string correlationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"correlation-{correlationId}");
    }

    public async Task RejoindreDefinition(string definitionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"definition-{definitionId}");
    }

    public async Task QuitterDefinition(string definitionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"definition-{definitionId}");
    }

    public override async Task OnConnectedAsync()
    {
        var user = Context.User?.Identity?.Name;
        if (user is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{user}");
        await base.OnConnectedAsync();
    }
}
