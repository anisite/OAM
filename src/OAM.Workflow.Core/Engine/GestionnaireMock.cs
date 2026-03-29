using System.Text.Json;
using OAM.Workflow.Core.Connecteurs;

namespace OAM.Workflow.Core.Engine;

/// <summary>
/// Résolution des mocks depuis un catalogue de réponses préconfigurées.
/// Les mocks sont définis dans les fichiers tests.[NOM].md.
/// </summary>
public class GestionnaireMock : IMockResolver
{
    private readonly Dictionary<string, List<MockEntry>> _catalogue = new(StringComparer.OrdinalIgnoreCase);

    public void ChargerMocks(Dictionary<string, object?> mocks)
    {
        foreach (var (id, valeur) in mocks)
        {
            var entries = new List<MockEntry>();
            if (valeur is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    entries.Add(new MockEntry(null, item.GetRawText()));
            }
            else
            {
                entries.Add(new MockEntry(null, JsonSerializer.Serialize(valeur)));
            }
            _catalogue[id] = entries;
        }
    }

    public void AjouterMock(string id, string? condition, string reponseJson)
    {
        if (!_catalogue.TryGetValue(id, out var entries))
        {
            entries = [];
            _catalogue[id] = entries;
        }
        entries.Add(new MockEntry(condition, reponseJson));
    }

    public Task<object?> ResoudreAsync(string mockId, Dictionary<string, object?> parametres)
    {
        if (!_catalogue.TryGetValue(mockId, out var entries) || entries.Count == 0)
            return Task.FromResult<object?>(null);

        // Chercher une entrée conditionnelle correspondante
        foreach (var entry in entries.Where(e => e.Condition is not null))
        {
            // TODO: Évaluer les conditions plus complexes
            if (parametres.Any(p => entry.Condition!.Contains(p.Key)))
                return Task.FromResult<object?>(JsonSerializer.Deserialize<object>(entry.ReponseJson));
        }

        // Retourner la première entrée par défaut
        var defaut = entries.FirstOrDefault(e => e.Condition is null) ?? entries[0];
        return Task.FromResult<object?>(JsonSerializer.Deserialize<object>(defaut.ReponseJson));
    }

    private record MockEntry(string? Condition, string ReponseJson);
}
