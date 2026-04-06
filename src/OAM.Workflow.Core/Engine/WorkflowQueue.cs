using System.Threading.Channels;

namespace OAM.Workflow.Core.Engine;

/// <summary>
/// File d'attente en mémoire pour les instances de workflow à traiter.
/// </summary>
public class WorkflowQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enfiler(Guid instanceId) => _channel.Writer.TryWrite(instanceId);

    public IAsyncEnumerable<Guid> LireAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
