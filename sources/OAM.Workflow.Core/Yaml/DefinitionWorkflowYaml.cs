using YamlDotNet.Serialization;

namespace OAM.Workflow.Core.Yaml;

/// <summary>
/// Représentation YAML d'une définition de workflow.
/// </summary>
public class DefinitionWorkflowYaml
{
    [YamlMember(Alias = "nom")]
    public required string Nom { get; set; }

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "equipe")]
    public string? Equipe { get; set; }

    [YamlMember(Alias = "variables")]
    public Dictionary<string, object>? Variables { get; set; }

    [YamlMember(Alias = "taches")]
    public required List<TacheYaml> Taches { get; set; }
}

public class TacheYaml
{
    [YamlMember(Alias = "id")]
    public required string Id { get; set; }

    [YamlMember(Alias = "nom")]
    public required string Nom { get; set; }

    [YamlMember(Alias = "type")]
    public required string Type { get; set; }

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "parametres")]
    public Dictionary<string, object>? Parametres { get; set; }

    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    [YamlMember(Alias = "suivant")]
    public string? Suivant { get; set; }

    [YamlMember(Alias = "enErreur")]
    public string? EnErreur { get; set; }

    [YamlMember(Alias = "boucle")]
    public BoucleYaml? Boucle { get; set; }

    [YamlMember(Alias = "branches")]
    public List<BrancheYaml>? Branches { get; set; }

    [YamlMember(Alias = "httpClientId")]
    public string? HttpClientId { get; set; }

    [YamlMember(Alias = "output")]
    public Dictionary<string, string>? Output { get; set; }

    [YamlMember(Alias = "hook")]
    public bool Hook { get; set; }
}

public class BoucleYaml
{
    [YamlMember(Alias = "collection")]
    public required string Collection { get; set; }

    [YamlMember(Alias = "variable")]
    public required string Variable { get; set; }
}

public class BrancheYaml
{
    [YamlMember(Alias = "condition")]
    public required string Condition { get; set; }

    [YamlMember(Alias = "aller")]
    public required string Aller { get; set; }
}
