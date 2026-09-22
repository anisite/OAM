using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OAM.Workflow.Core.Yaml;

public static class YamlParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static DefinitionWorkflowYaml ParseDefinition(string yaml) =>
        Deserializer.Deserialize<DefinitionWorkflowYaml>(yaml);

    public static ExtensionsYaml ParseExtensions(string yaml) =>
        Deserializer.Deserialize<ExtensionsYaml>(yaml);

    public static string Serialiser<T>(T objet) =>
        Serializer.Serialize(objet);

    public static string CalculerHash(string contenu)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(contenu));
        return Convert.ToHexStringLower(hash);
    }

    public static string CalculerHashCombine(string workflow, string? extensions = null, string? httpClients = null)
    {
        var contenu = workflow + (extensions ?? "") + (httpClients ?? "");
        return CalculerHash(contenu);
    }
}
