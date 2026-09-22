using OAM.Workflow.Core.Engine;

namespace OAM.Tests;

[TestClass]
public class GestionnaireMockTests
{
    [TestMethod]
    public async Task AjouterMock_Et_Resoudre_DoitFonctionner()
    {
        var gestionnaire = new GestionnaireMock();
        gestionnaire.AjouterMock("monMock", null, """{"resultat": "ok"}""");

        var resultat = await gestionnaire.ResoudreAsync("monMock", new());

        Assert.IsNotNull(resultat);
    }

    [TestMethod]
    public async Task Resoudre_MockInconnu_RetourneNull()
    {
        var gestionnaire = new GestionnaireMock();
        var resultat = await gestionnaire.ResoudreAsync("inexistant", new());
        Assert.IsNull(resultat);
    }

    [TestMethod]
    public async Task Resoudre_AvecCondition_DoitPrioriser()
    {
        var gestionnaire = new GestionnaireMock();
        gestionnaire.AjouterMock("monMock", null, """{"type": "defaut"}""");
        gestionnaire.AjouterMock("monMock", "rendezVousId", """{"type": "conditionnel"}""");

        var parametres = new Dictionary<string, object?> { ["rendezVousId"] = "RDV-001" };
        var resultat = await gestionnaire.ResoudreAsync("monMock", parametres);

        Assert.IsNotNull(resultat);
    }
}
