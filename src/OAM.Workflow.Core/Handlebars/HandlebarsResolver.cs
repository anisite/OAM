using HandlebarsDotNet;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Path;

namespace OAM.Workflow.Core.Handlebars;

/// <summary>
/// Résolution dynamique des variables entre étapes via Handlebars + JSONPath.
/// Syntaxe : {{ tache.nomTache.output.chemin.vers.valeur }}
/// </summary>
public class HandlebarsResolver
{
    private static readonly IHandlebars Engine;

    static HandlebarsResolver()
    {
        Engine = HandlebarsDotNet.Handlebars.Create();

        Engine.RegisterHelper("jsonpath", (output, context, arguments) =>
        {
            if (arguments.Length < 2) return;
            var json = arguments[0]?.ToString();
            var path = arguments[1]?.ToString();
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(path)) return;

            var node = JsonNode.Parse(json);
            var jsonPath = JsonPath.Parse(path);
            var result = jsonPath.Evaluate(node);
            if (result.Matches?.Count > 0)
            {
                output.WriteSafeString(result.Matches[0].Value?.ToString() ?? "");
            }
        });
    }

    public static string Resoudre(string template, Dictionary<string, object?> contexte)
    {
        var compiled = Engine.Compile(template);
        return compiled(contexte);
    }

    public static string ResoudreParametre(string valeur, Dictionary<string, object?> contexteExecution)
    {
        if (!valeur.Contains("{{"))
            return valeur;

        return Resoudre(valeur, contexteExecution);
    }

    public static Dictionary<string, object?> ResoudreParametres(
        Dictionary<string, object>? parametres,
        Dictionary<string, object?> contexteExecution)
    {
        var resolus = new Dictionary<string, object?>();
        if (parametres is null) return resolus;

        foreach (var (cle, valeur) in parametres)
        {
            if (valeur is string s)
                resolus[cle] = ResoudreParametre(s, contexteExecution);
            else
                resolus[cle] = valeur;
        }
        return resolus;
    }

    public static object? ExtraireJsonPath(string json, string path)
    {
        var node = JsonNode.Parse(json);
        var jsonPath = JsonPath.Parse(path);
        var result = jsonPath.Evaluate(node);

        if (result.Matches is null || result.Matches.Count == 0)
            return null;

        return result.Matches.Count == 1
            ? JsonNodeToObject(result.Matches[0].Value)
            : result.Matches.Select(m => JsonNodeToObject(m.Value)).ToList();
    }

    private static object? JsonNodeToObject(JsonNode? node)
    {
        if (node is null) return null;

        return node switch
        {
            JsonValue val when val.TryGetValue<string>(out var s) => s,
            JsonValue val when val.TryGetValue<long>(out var l) => l,
            JsonValue val when val.TryGetValue<double>(out var d) => d,
            JsonValue val when val.TryGetValue<bool>(out var b) => b,
            _ => node.ToJsonString()
        };
    }
}
