using OAM.Workflow.Core.Handlebars;

namespace OAM.Tests;

[TestClass]
public class HandlebarsResolverTests
{
    [TestMethod]
    public void Resoudre_DoitRemplacerVariableSimple()
    {
        var contexte = new Dictionary<string, object?> { ["nom"] = "OAM" };
        var resultat = HandlebarsResolver.Resoudre("Bonjour {{nom}}", contexte);
        Assert.AreEqual("Bonjour OAM", resultat);
    }

    [TestMethod]
    public void Resoudre_DoitGererVariableImbriquee()
    {
        var contexte = new Dictionary<string, object?>
        {
            ["tache"] = new Dictionary<string, object?>
            {
                ["etape1"] = new Dictionary<string, object?>
                {
                    ["output"] = "valeur-sortie"
                }
            }
        };

        var resultat = HandlebarsResolver.Resoudre("{{tache.etape1.output}}", contexte);
        Assert.AreEqual("valeur-sortie", resultat);
    }

    [TestMethod]
    public void ResoudreParametre_SansTemplate_RetourneTelQuel()
    {
        var resultat = HandlebarsResolver.ResoudreParametre("texte simple", new());
        Assert.AreEqual("texte simple", resultat);
    }

    [TestMethod]
    public void ResoudreParametres_DoitResoudreTousLesParametres()
    {
        var parametres = new Dictionary<string, object>
        {
            ["url"] = "https://api.com/{{id}}",
            ["fixe"] = "valeur-fixe",
            ["nombre"] = 42
        };
        var contexte = new Dictionary<string, object?> { ["id"] = "123" };

        var resolus = HandlebarsResolver.ResoudreParametres(parametres, contexte);

        Assert.AreEqual("https://api.com/123", resolus["url"]);
        Assert.AreEqual("valeur-fixe", resolus["fixe"]);
        Assert.AreEqual(42, resolus["nombre"]);
    }

    [TestMethod]
    public void ExtraireJsonPath_DoitExtraireValeur()
    {
        var json = """{"data":{"rendezVous":{"id":"RDV-001","client":"Jean"}}}""";
        var resultat = HandlebarsResolver.ExtraireJsonPath(json, "$.data.rendezVous.client");
        Assert.AreEqual("Jean", resultat);
    }

    [TestMethod]
    public void ExtraireJsonPath_Introuvable_RetourneNull()
    {
        var json = """{"data":{}}""";
        var resultat = HandlebarsResolver.ExtraireJsonPath(json, "$.data.inexistant");
        Assert.IsNull(resultat);
    }
}
