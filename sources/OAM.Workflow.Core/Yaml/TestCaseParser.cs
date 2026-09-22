using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OAM.Workflow.Core.Yaml;

public class TestCaseResult
{
    public string? InputJson { get; init; }
    public List<(string Id, string ReponseJson)> Mocks { get; init; } = [];
    public OutputAttendu? OutputAttendu { get; init; }
}

public class OutputAttendu
{
    public string? Etat { get; init; }
    public Dictionary<string, TacheAttendue> Taches { get; init; } = [];
}

public class TacheAttendue
{
    public string? Etat { get; init; }
    public Dictionary<string, string?>? Input { get; init; }
}

public static partial class TestCaseParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlJsonSerializer = new SerializerBuilder()
        .JsonCompatible()
        .Build();

    [GeneratedRegex(@"^(?=###[^#])", RegexOptions.Multiline)]
    private static partial Regex SplitSectionsRegex();

    [GeneratedRegex(@"```(?:yaml)?\r?\n([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex BlocYamlRegex();

    [GeneratedRegex(@"^####(?!#)\s+(?<id>\S+)[^\n]*\n(?<suite>[\s\S]*?)(?=^####(?!#)|^###(?!#)|\z)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SectionMockRegex();

    public static TestCaseResult Analyser(string markdown)
    {
        markdown = markdown.Replace("\r\n", "\n").Replace("\r", "\n");

        string? inputJson = null;
        var mocks = new List<(string Id, string ReponseJson)>();
        OutputAttendu? outputAttendu = null;

        foreach (var section in SplitSectionsRegex().Split(markdown))
        {
            if (Regex.IsMatch(section, @"^###\s+INPUT", RegexOptions.IgnoreCase))
            {
                var bloc = BlocYamlRegex().Match(section);
                if (bloc.Success)
                    inputJson = YamlVersJson(bloc.Groups[1].Value.Trim());
            }
            else if (Regex.IsMatch(section, @"^###\s+Mocks?", RegexOptions.IgnoreCase))
            {
                foreach (Match m in SectionMockRegex().Matches(section))
                {
                    var id = m.Groups["id"].Value.Trim();
                    var bloc = BlocYamlRegex().Match(m.Groups["suite"].Value);
                    if (!bloc.Success) continue;
                    mocks.Add((id, YamlVersJson(bloc.Groups[1].Value.Trim())));
                }
            }
            else if (Regex.IsMatch(section, @"^###\s+OUTPUT\s+attendu", RegexOptions.IgnoreCase))
            {
                var bloc = BlocYamlRegex().Match(section);
                if (bloc.Success)
                    outputAttendu = ParseOutputAttendu(bloc.Groups[1].Value.Trim());
            }
        }

        return new TestCaseResult { InputJson = inputJson, Mocks = mocks, OutputAttendu = outputAttendu };
    }

    private static OutputAttendu ParseOutputAttendu(string yaml)
    {
        var raw = Deserializer.Deserialize<Dictionary<object, object?>>(yaml);
        var etat = raw.GetValueOrDefault("etat")?.ToString();
        var taches = new Dictionary<string, TacheAttendue>(StringComparer.OrdinalIgnoreCase);

        if (raw.TryGetValue("taches", out var tachesObj) && tachesObj is Dictionary<object, object?> tachesDict)
        {
            foreach (var (nomTache, valTache) in tachesDict)
            {
                if (valTache is not Dictionary<object, object?> tacheProps) continue;

                var etatTache = tacheProps.GetValueOrDefault("etat")?.ToString();
                Dictionary<string, string?>? inputAttendu = null;

                if (tacheProps.TryGetValue("input", out var inputObj) && inputObj is Dictionary<object, object?> inputDict)
                    inputAttendu = inputDict.ToDictionary(
                        kv => kv.Key.ToString()!,
                        kv => kv.Value?.ToString());

                taches[nomTache.ToString()!] = new TacheAttendue { Etat = etatTache, Input = inputAttendu };
            }
        }

        return new OutputAttendu { Etat = etat, Taches = taches };
    }

    private static string YamlVersJson(string yaml)
    {
        var obj = Deserializer.Deserialize<object>(yaml);
        return YamlJsonSerializer.Serialize(obj);
    }
}
