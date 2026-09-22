using System.Collections.Concurrent;

namespace OAM.Workflow.Core.Engine;

/// <summary>
/// Implémentation en mémoire — serveur unique.
/// Remplacer par AttenteReponseGarnet pour le multi-serveurs.
/// </summary>
public class AttenteReponseMemoire : IAttenteReponse
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object?>> _attentes = new();

    public Task<object?> AttendrePourAsync(string correlationId, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _attentes[correlationId] = tcs;

        _ = Task.Delay(timeout).ContinueWith(_ =>
        {
            if (_attentes.TryRemove(correlationId, out var t))
                t.TrySetResult(null); // timeout = null, le caller retourne l'instanceId
        });

        return tcs.Task;
    }

    public void Signaler(string correlationId, object? donnees)
    {
        if (_attentes.TryRemove(correlationId, out var tcs))
            tcs.TrySetResult(donnees);
    }
}
