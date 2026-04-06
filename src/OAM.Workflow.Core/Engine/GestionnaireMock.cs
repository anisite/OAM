using System.Text.Json;
using OAM.Workflow.Core.Connecteurs;

namespace OAM.Workflow.Core.Engine;

/// <summary>
/// Résolution des mocks depuis un catalogue de réponses préconfigurées.
/// Les mocks sont scopés par correlationId pour permettre l'exécution parallèle.
/// </summary>
public class GestionnaireMock : IMockResolver
{
    private readonly Dictionary<string, Dictionary<string, List<MockEntry>>> _catalogue = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public void AjouterMock(string id, string correlationId, string? condition, string reponseJson)
    {
        lock (_lock)
        {
            if (!_catalogue.TryGetValue(correlationId, out var parTache))
            {
                parTache = new Dictionary<string, List<MockEntry>>(StringComparer.OrdinalIgnoreCase);
                _catalogue[correlationId] = parTache;
            }

            if (!parTache.TryGetValue(id, out var entries))
            {
                entries = [];
                parTache[id] = entries;
            }

            entries.Add(new MockEntry(condition, reponseJson));
        }
    }

    public void ViderMocks(string correlationId)
    {
        lock (_lock) { _catalogue.Remove(correlationId); }
    }

    public void RetirerMock(string id, string correlationId)
    {
        lock (_lock)
        {
            if (_catalogue.TryGetValue(correlationId, out var parTache))
            {
                parTache.Remove(id);
                if (parTache.Count == 0)
                    _catalogue.Remove(correlationId);
            }
        }
    }

    public const string Global = "global";

    public Task<object?> ResoudreAsync(string mockId, string correlationId, Dictionary<string, object?> parametres)
    {
        lock (_lock)
        {
            // Priorité : mocks scopés au correlationId, sinon mocks globaux
            foreach (var scope in new[] { correlationId, Global })
            {
                if (!_catalogue.TryGetValue(scope, out var parTache) ||
                    !parTache.TryGetValue(mockId, out var entries) || entries.Count == 0)
                    continue;

                foreach (var entry in entries.Where(e => e.Condition is not null))
                {
                    if (parametres.Any(p => entry.Condition!.Contains(p.Key)))
                        return Task.FromResult<object?>(JsonSerializer.Deserialize<object>(entry.ReponseJson));
                }

                var defaut = entries.FirstOrDefault(e => e.Condition is null) ?? entries[0];
                return Task.FromResult<object?>(JsonSerializer.Deserialize<object>(defaut.ReponseJson));
            }
        }

        return Task.FromResult<object?>(null);
    }

    private record MockEntry(string? Condition, string ReponseJson);
}
