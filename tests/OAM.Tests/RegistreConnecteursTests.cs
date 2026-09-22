using OAM.Domain.Interfaces;
using OAM.Workflow.Core.Engine;

namespace OAM.Tests;

[TestClass]
public class RegistreConnecteursTests
{
    [TestMethod]
    public void Enregistrer_Et_Obtenir_DoitFonctionner()
    {
        var registre = new RegistreConnecteurs();
        var connecteur = new FakeConnecteur("test");

        registre.Enregistrer(connecteur);

        var obtenu = registre.Obtenir("test");
        Assert.IsNotNull(obtenu);
        Assert.AreEqual("test", obtenu.Type);
    }

    [TestMethod]
    public void Obtenir_TypeInconnu_RetourneNull()
    {
        var registre = new RegistreConnecteurs();
        Assert.IsNull(registre.Obtenir("inconnu"));
    }

    [TestMethod]
    public void Obtenir_EstCaseInsensitive()
    {
        var registre = new RegistreConnecteurs();
        registre.Enregistrer(new FakeConnecteur("Http"));

        Assert.IsNotNull(registre.Obtenir("http"));
        Assert.IsNotNull(registre.Obtenir("HTTP"));
    }

    private class FakeConnecteur(string type) : IConnecteur
    {
        public string Type => type;
        public Task<ResultatConnecteur> ExecuterAsync(ContexteConnecteur contexte) =>
            Task.FromResult(new ResultatConnecteur(true, null));
    }
}
