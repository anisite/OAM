using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OIM.Moteur.Definitions;

/// <summary>
/// Conversion YAML → JSON (System.Text.Json.Nodes).
/// Les scalaires non entre guillemets suivent le schéma « core » de YAML 1.2 :
/// true/false, null/~, entiers et décimaux sont typés; tout le reste est texte
/// (ex. <c>00:00:30</c> reste une chaîne). Un scalaire entre guillemets est toujours du texte.
/// </summary>
public static partial class YamlJson
{
    [GeneratedRegex(@"^[-+]?\d+$")]
    private static partial Regex Entier();

    [GeneratedRegex(@"^[-+]?(\d+\.\d*|\.\d+|\d+)([eE][-+]?\d+)?$")]
    private static partial Regex Decimal();

    public static JsonNode? Lire(string yaml)
    {
        var flux = new YamlStream();
        using var lecteur = new StringReader(yaml);
        flux.Load(lecteur);
        return flux.Documents.Count == 0 ? null : Convertir(flux.Documents[0].RootNode);
    }

    public static JsonNode? Convertir(YamlNode noeud) => noeud switch
    {
        YamlMappingNode map => ConvertirMap(map),
        YamlSequenceNode seq => new JsonArray(seq.Children.Select(Convertir).ToArray()),
        YamlScalarNode s => ConvertirScalaire(s),
        _ => null
    };

    private static JsonObject ConvertirMap(YamlMappingNode map)
    {
        var obj = new JsonObject();
        foreach (var (cle, valeur) in map.Children)
        {
            var nom = (cle as YamlScalarNode)?.Value
                      ?? throw new YamlException(cle.Start, cle.End, "Les clés doivent être des scalaires.");
            if (obj.ContainsKey(nom))
                throw new YamlException(cle.Start, cle.End, $"Clé « {nom} » en double.");
            obj[nom] = Convertir(valeur);
        }
        return obj;
    }

    private static JsonNode? ConvertirScalaire(YamlScalarNode s)
    {
        var v = s.Value;
        if (s.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded)
            return JsonValue.Create(v ?? string.Empty);

        if (v is null or "" or "~" or "null" or "Null" or "NULL") return null;
        if (v is "true" or "True" or "TRUE") return JsonValue.Create(true);
        if (v is "false" or "False" or "FALSE") return JsonValue.Create(false);
        if (Entier().IsMatch(v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            return l is >= int.MinValue and <= int.MaxValue ? JsonValue.Create((int)l) : JsonValue.Create(l);
        if (Decimal().IsMatch(v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return JsonValue.Create(d);
        return JsonValue.Create(v);
    }
}
