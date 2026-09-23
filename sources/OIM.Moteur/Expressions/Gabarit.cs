using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OIM.Moteur.Expressions;

/// <summary>
/// Résolution des valeurs contenant des expressions <c>{{ … }}</c>.
/// <list type="bullet">
///   <item>Une chaîne constituée d'une seule expression (<c>"{{ entrees.dossierId }}"</c>)
///   conserve le type du résultat (nombre, booléen, objet…).</item>
///   <item>Sinon, chaque expression est convertie en texte et interpolée
///   (<c>"Confirmation #{{ etapes.valider.sortie.numero }}"</c>).</item>
///   <item>Les objets et listes sont résolus récursivement.</item>
/// </list>
/// </summary>
public static partial class Gabarit
{
    [GeneratedRegex(@"\{\{(?<expr>.*?)\}\}", RegexOptions.Singleline)]
    private static partial Regex Motif();

    [GeneratedRegex(@"^\s*\{\{(?<expr>(?:(?!\}\}).)*)\}\}\s*$", RegexOptions.Singleline)]
    private static partial Regex MotifUnique();

    public static bool ContientExpression(string? texte) => texte is not null && texte.Contains("{{");

    public static JsonNode? Resoudre(JsonNode? valeur, JsonObject contexte) => valeur switch
    {
        null => null,
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, Resoudre(p.Value, contexte)))),
        JsonArray a => new JsonArray(a.Select(x => Resoudre(x, contexte)).ToArray()),
        JsonValue v when v.TryGetValue<string>(out var s) => ResoudreTexte(s, contexte),
        _ => valeur.DeepClone()
    };

    public static JsonNode? ResoudreTexte(string texte, JsonObject contexte)
    {
        if (!ContientExpression(texte)) return JsonValue.Create(texte);

        var unique = MotifUnique().Match(texte);
        if (unique.Success)
            return Expression.Analyser(unique.Groups["expr"].Value.Trim()).Evaluer(contexte);

        return JsonValue.Create(Interpoler(texte, contexte));
    }

    public static string Interpoler(string texte, JsonObject contexte) =>
        Motif().Replace(texte, m => Expression.EnTexte(Expression.Analyser(m.Groups["expr"].Value.Trim()).Evaluer(contexte)));

    /// <summary>Évalue une condition (avec ou sans accolades) en booléen.</summary>
    public static bool Condition(string condition, JsonObject contexte)
    {
        var unique = MotifUnique().Match(condition);
        var source = unique.Success ? unique.Groups["expr"].Value : condition;
        return Expression.EstVrai(Expression.Analyser(source.Trim()).Evaluer(contexte));
    }

    /// <summary>Extrait toutes les expressions d'une valeur (pour la validation).</summary>
    public static IEnumerable<string> Expressions(JsonNode? valeur)
    {
        switch (valeur)
        {
            case JsonObject o:
                foreach (var p in o)
                foreach (var e in Expressions(p.Value)) yield return e;
                break;
            case JsonArray a:
                foreach (var x in a)
                foreach (var e in Expressions(x)) yield return e;
                break;
            case JsonValue v when v.TryGetValue<string>(out var s):
                foreach (Match m in Motif().Matches(s)) yield return m.Groups["expr"].Value.Trim();
                break;
        }
    }

    /// <summary>Retire les accolades d'une condition, si présentes.</summary>
    public static string SourceCondition(string condition)
    {
        var unique = MotifUnique().Match(condition);
        return (unique.Success ? unique.Groups["expr"].Value : condition).Trim();
    }

    public static string Tronquer(string s, int max) =>
        s.Length <= max ? s : new StringBuilder(s, 0, max, max + 1).Append('…').ToString();
}
