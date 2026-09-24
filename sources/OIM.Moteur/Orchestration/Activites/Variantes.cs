using System.Text.Json.Nodes;

namespace OIM.Moteur.Orchestration.Activites;

/// <summary>
/// Valeurs déclinées par palier ou par langue, comme dans les configurations ECS25A :
/// <c>{ unitaire: …, acceptation: …, production: … }</c>, <c>{ fr: …, en: … }</c> ou <c>{ tous: … }</c>.
/// Une valeur qui n'est pas un objet est retournée telle quelle.
/// </summary>
public static class Variantes
{
    public static JsonNode? Choisir(JsonNode? valeur, params string?[] cles)
    {
        if (valeur is not JsonObject o) return valeur;
        foreach (var cle in cles.Append("tous").Append("defaut"))
            if (cle is not null && o.TryGetPropertyValue(cle, out var v)) return v;
        return null;
    }

    /// <summary>Objet de variantes reconnu (au moins une clé de palier ou de langue).</summary>
    public static bool EstDeclinee(JsonNode? valeur, string palier) =>
        valeur is JsonObject o && (o.ContainsKey(palier) || o.ContainsKey("tous") || o.ContainsKey("defaut") || o.ContainsKey("fr") || o.ContainsKey("en"));
}
