using YamlDotNet.Serialization;

namespace OAM.Workflow.Core.Yaml;

/// <summary>
/// Extensions (Actions Métier) définies en YAML.
/// </summary>
public class ExtensionsYaml
{
    [YamlMember(Alias = "extensions")]
    public List<ActionMetierYaml>? Extensions { get; set; }
}

public class ActionMetierYaml
{
    [YamlMember(Alias = "id")]
    public required string Id { get; set; }

    [YamlMember(Alias = "type")]
    public required string Type { get; set; }

    [YamlMember(Alias = "httpClientId")]
    public string? HttpClientId { get; set; }

    [YamlMember(Alias = "mock")]
    public string? Mock { get; set; }

    [YamlMember(Alias = "famille")]
    public string? Famille { get; set; }

    [YamlMember(Alias = "output")]
    public Dictionary<string, string>? Output { get; set; }

    [YamlMember(Alias = "parametres")]
    public Dictionary<string, object>? Parametres { get; set; }

    [YamlMember(Alias = "default")]
    public Dictionary<string, object>? Default { get; set; }
}
