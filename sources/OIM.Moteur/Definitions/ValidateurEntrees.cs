using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OIM.Moteur.Definitions;

/// <summary>
/// Contrôle et normalise les entrées d'une instance selon la section <c>entrees</c> :
/// champs requis, valeurs par défaut et conversion de type (ex. "12" → 12 pour un int).
/// Les champs non déclarés sont conservés tels quels.
/// </summary>
public static class ValidateurEntrees
{
    public static (JsonObject Entrees, List<string> Erreurs) Normaliser(DefinitionProcessus definition, JsonObject? recues)
    {
        var resultat = recues?.DeepClone().AsObject() ?? [];
        var erreurs = new List<string>();

        foreach (var p in definition.Entrees)
        {
            var valeur = Trouver(resultat, p.Nom, out var cle);
            if (valeur is null || valeur is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Length == 0 && p.Type != "string")
            {
                if (p.Defaut is not null)
                    resultat[cle ?? p.Nom] = p.Defaut.DeepClone();
                else if (p.Requis)
                    erreurs.Add($"L'entrée « {p.Nom} » est obligatoire.");
                continue;
            }

            if (Convertir(valeur, p.Type) is { } convertie)
                resultat[cle!] = convertie;
            else
                erreurs.Add($"L'entrée « {p.Nom} » doit être de type {p.Type} (reçu : {Gabarit(valeur)}).");
        }

        return (resultat, erreurs);
    }

    private static string Gabarit(JsonNode n) => Expressions.Gabarit.Tronquer(n.ToJsonString(), 60);

    private static JsonNode? Trouver(JsonObject o, string nom, out string? cle)
    {
        foreach (var (k, v) in o)
            if (string.Equals(k, nom, StringComparison.OrdinalIgnoreCase))
            {
                cle = k;
                return v;
            }
        cle = null;
        return null;
    }

    private static JsonNode? Convertir(JsonNode valeur, string type)
    {
        var kind = valeur.GetValueKind();
        var texte = kind == JsonValueKind.String ? valeur.GetValue<string>().Trim() : null;

        switch (type)
        {
            case "string":
                return kind == JsonValueKind.String ? valeur.DeepClone()
                    : valeur is JsonValue ? JsonValue.Create(Expressions.Expression.EnTexte(valeur)) : null;

            case "int":
                if (kind == JsonValueKind.Number && valeur.AsValue().TryGetValue<long>(out var l)) return JsonValue.Create(l);
                if (kind == JsonValueKind.Number && Expressions.Expression.Double(valeur.AsValue()) is var d && d == Math.Floor(d)) return JsonValue.Create((long)d);
                return long.TryParse(texte, NumberStyles.Integer, CultureInfo.InvariantCulture, out var li) ? JsonValue.Create(li) : null;

            case "number":
                if (kind == JsonValueKind.Number) return valeur.DeepClone();
                return double.TryParse(texte?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n) : null;

            case "bool":
                if (kind is JsonValueKind.True or JsonValueKind.False) return valeur.DeepClone();
                return texte?.ToLowerInvariant() switch
                {
                    "true" or "vrai" or "oui" or "1" => JsonValue.Create(true),
                    "false" or "faux" or "non" or "0" => JsonValue.Create(false),
                    _ => null
                };

            case "date":
                return texte is not null && DateTimeOffset.TryParse(texte, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)
                    ? JsonValue.Create(texte)
                    : null;

            case "object":
                if (valeur is JsonObject) return valeur.DeepClone();
                return TenterJson(texte) as JsonObject;

            case "array":
                if (valeur is JsonArray) return valeur.DeepClone();
                return TenterJson(texte) as JsonArray;

            default:
                return valeur.DeepClone();
        }
    }

    private static JsonNode? TenterJson(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;
        try { return JsonNode.Parse(texte); }
        catch (JsonException) { return null; }
    }
}
