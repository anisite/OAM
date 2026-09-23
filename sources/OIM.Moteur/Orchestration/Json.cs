using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;

namespace OIM.Moteur.Orchestration;

/// <summary>
/// Frontière entre DurableTask.Core (sérialisation Newtonsoft) et le moteur (System.Text.Json).
/// <list type="bullet">
///   <item>Entrées d'orchestration et événements : JSON brut (<see cref="JRaw"/>) — lisible tel quel
///   dans dt.Payloads et le tableau de bord.</item>
///   <item>Activités : le JSON voyage sous forme de chaîne, ce qui évite toute interprétation
///   Newtonsoft (ex. clés « $type » dans une réponse HTTP).</item>
/// </list>
/// </summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static JRaw Brut(JsonNode? noeud) => new(noeud?.ToJsonString() ?? "null");

    public static JsonNode? Lire(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;
        try
        {
            return JsonNode.Parse(texte);
        }
        catch (JsonException)
        {
            return JsonValue.Create(texte);
        }
    }

    /// <summary>Lit l'entrée d'une activité : tableau de paramètres DurableTask contenant une chaîne JSON.</summary>
    public static JsonObject LireParametreActivite(string entree)
    {
        var tableau = JArray.Parse(entree);
        var texte = tableau.Count > 0 ? tableau[0].Type == JTokenType.String ? tableau[0].Value<string>() : tableau[0].ToString(Newtonsoft.Json.Formatting.None) : "{}";
        return JsonNode.Parse(texte ?? "{}") as JsonObject ?? [];
    }

    /// <summary>Résultat d'activité : une chaîne JSON contenant le JSON du résultat.</summary>
    public static string EcrireResultatActivite(JsonNode? resultat) =>
        JsonSerializer.Serialize(resultat?.ToJsonString() ?? "null");

    public static JsonNode? DepuisJToken(JToken? jeton) =>
        jeton is null || jeton.Type == JTokenType.Null ? null : JsonNode.Parse(jeton.ToString(Newtonsoft.Json.Formatting.None));

    /// <summary>Conversion vers des types .NET simples (dictionnaires, listes) pour Handlebars.</summary>
    public static object? VersObjet(JsonNode? noeud) => noeud switch
    {
        null => null,
        JsonObject o => o.ToDictionary(p => p.Key, p => VersObjet(p.Value)),
        JsonArray a => a.Select(VersObjet).ToList(),
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetValue<long>(out var l) ? l : v.TryGetValue<int>(out var i) ? i : Expressions.Expression.Double(v),
            _ => null
        },
        _ => null
    };
}
