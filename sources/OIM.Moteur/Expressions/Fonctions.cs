using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OIM.Moteur.Expressions;

/// <summary>
/// Fonctions disponibles dans les expressions. Toutes sont pures (aucune horloge, aucun accès
/// externe) : l'heure courante est fournie par la variable de contexte <c>maintenant</c>.
/// </summary>
public static class Fonctions
{
    private static readonly Dictionary<string, Func<JsonNode?[], JsonNode?>> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["longueur"] = a => JsonValue.Create(Arg(a, 0) switch
        {
            JsonArray t => t.Count,
            JsonObject o => o.Count,
            var v => Expression.EnTexte(v).Length
        }),
        ["vide"] = a => JsonValue.Create(EstVide(Arg(a, 0))),
        ["contient"] = a => JsonValue.Create(Arg(a, 0) switch
        {
            JsonArray t => t.Any(x => Expression.Egal(x, Arg(a, 1))),
            JsonObject o => o.ContainsKey(Expression.EnTexte(Arg(a, 1))),
            var v => Expression.EnTexte(v).Contains(Expression.EnTexte(Arg(a, 1)), StringComparison.OrdinalIgnoreCase)
        }),
        ["commencePar"] = a => JsonValue.Create(Expression.EnTexte(Arg(a, 0)).StartsWith(Expression.EnTexte(Arg(a, 1)), StringComparison.OrdinalIgnoreCase)),
        ["minuscule"] = a => JsonValue.Create(Expression.EnTexte(Arg(a, 0)).ToLowerInvariant()),
        ["majuscule"] = a => JsonValue.Create(Expression.EnTexte(Arg(a, 0)).ToUpperInvariant()),
        ["texte"] = a => JsonValue.Create(Expression.EnTexte(Arg(a, 0))),
        ["nombre"] = a => Expression.Valeur(Expression.Nombre(Arg(a, 0))),
        ["arrondi"] = a => Expression.Valeur(Math.Round(Expression.Nombre(Arg(a, 0)), (int)Expression.Nombre(Arg(a, 1)), MidpointRounding.AwayFromZero)),
        ["premier"] = a => a.FirstOrDefault(x => x is not null && Expression.EnTexte(x).Length > 0)?.DeepClone(),
        ["json"] = a => JsonValue.Create(Arg(a, 0)?.ToJsonString() ?? "null"),
        ["joindre"] = a => JsonValue.Create(Arg(a, 0) is JsonArray t
            ? string.Join(Arg(a, 1) is null ? ", " : Expression.EnTexte(Arg(a, 1)), t.Select(Expression.EnTexte))
            : Expression.EnTexte(Arg(a, 0))),
        ["ajouterJours"] = a => AjouterDuree(a, TimeSpan.FromDays(1)),
        ["ajouterHeures"] = a => AjouterDuree(a, TimeSpan.FromHours(1)),
        ["formaterDate"] = a => JsonValue.Create(LireDate(Arg(a, 0)) is { } d
            ? d.ToString(a.Length > 1 ? Expression.EnTexte(a[1]) : "yyyy-MM-dd", CultureInfo.GetCultureInfo("fr-CA"))
            : string.Empty)
    };

    public static IEnumerable<string> Noms => Table.Keys;

    public static Func<JsonNode?[], JsonNode?>? Trouver(string nom) => Table.GetValueOrDefault(nom);

    private static JsonNode? Arg(JsonNode?[] a, int i) => i < a.Length ? a[i] : null;

    private static bool EstVide(JsonNode? v) => v switch
    {
        null => true,
        JsonArray t => t.Count == 0,
        JsonObject o => o.Count == 0,
        JsonValue val when val.GetValueKind() == JsonValueKind.String => val.GetValue<string>().Trim().Length == 0,
        _ => false
    };

    private static DateTimeOffset? LireDate(JsonNode? v) =>
        DateTimeOffset.TryParse(Expression.EnTexte(v), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static JsonNode? AjouterDuree(JsonNode?[] a, TimeSpan unite) =>
        LireDate(Arg(a, 0)) is { } d
            ? JsonValue.Create(d.Add(unite * Expression.Nombre(Arg(a, 1))).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
            : null;
}
