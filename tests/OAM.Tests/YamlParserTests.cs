using OAM.Workflow.Core.Yaml;

namespace OAM.Tests;

[TestClass]
public class YamlParserTests
{
    private const string WorkflowYaml = """
        nom: TestWorkflow
        description: Workflow de test
        equipe: QA
        taches:
          - id: etape1
            nom: Première étape
            type: http
            httpClientId: monApi
            parametres:
              url: https://example.com
          - id: etape2
            nom: Deuxième étape
            type: condition
            parametres:
              expression: "true"
            suivant: etape3
          - id: etape3
            nom: Troisième étape
            type: hook
            hook: true
        """;

    [TestMethod]
    public void ParseDefinition_DoitRetournerDefinitionValide()
    {
        var definition = YamlParser.ParseDefinition(WorkflowYaml);

        Assert.AreEqual("TestWorkflow", definition.Nom);
        Assert.AreEqual("Workflow de test", definition.Description);
        Assert.AreEqual("QA", definition.Equipe);
        Assert.AreEqual(3, definition.Taches.Count);
    }

    [TestMethod]
    public void ParseDefinition_DoitParserLesTaches()
    {
        var definition = YamlParser.ParseDefinition(WorkflowYaml);

        var tache1 = definition.Taches[0];
        Assert.AreEqual("etape1", tache1.Id);
        Assert.AreEqual("http", tache1.Type);
        Assert.AreEqual("monApi", tache1.HttpClientId);

        var tache2 = definition.Taches[1];
        Assert.AreEqual("condition", tache2.Type);
        Assert.AreEqual("etape3", tache2.Suivant);

        var tache3 = definition.Taches[2];
        Assert.IsTrue(tache3.Hook);
    }

    [TestMethod]
    public void CalculerHash_DoitRetournerHashConsistant()
    {
        var hash1 = YamlParser.CalculerHash(WorkflowYaml);
        var hash2 = YamlParser.CalculerHash(WorkflowYaml);

        Assert.AreEqual(hash1, hash2);
        Assert.AreEqual(64, hash1.Length); // SHA256 hex
    }

    [TestMethod]
    public void CalculerHash_DoitDiffererPourContenuDifferent()
    {
        var hash1 = YamlParser.CalculerHash("contenu A");
        var hash2 = YamlParser.CalculerHash("contenu B");

        Assert.AreNotEqual(hash1, hash2);
    }

    [TestMethod]
    public void ParseExtensions_DoitParserLesActionMetier()
    {
        const string yaml = """
            extensions:
              - id: rechercherRendezVous
                type: http
                httpClientId: getRendezVousApi
                mock: rechercherRendezVousMock
                famille: Agenda
                output:
                  rendezVous: "$.data.rendezVous"
            """;

        var extensions = YamlParser.ParseExtensions(yaml);

        Assert.IsNotNull(extensions.Extensions);
        Assert.AreEqual(1, extensions.Extensions.Count);
        Assert.AreEqual("rechercherRendezVous", extensions.Extensions[0].Id);
        Assert.AreEqual("Agenda", extensions.Extensions[0].Famille);
    }
}
