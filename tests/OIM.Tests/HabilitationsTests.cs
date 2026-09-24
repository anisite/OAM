using OIM.Moteur.Pilotage;

namespace OIM.Tests;

[TestClass]
public sealed class HabilitationsTests
{
    private static readonly Habilitations Membre = new("alice", false, false, new HashSet<string> { "a" });
    private static readonly Habilitations Support = new("sup", false, true, new HashSet<string>());
    private static readonly Habilitations Admin = new("adm", true, false, new HashSet<string>());
    private static readonly Habilitations Aucun = new("x", false, false, new HashSet<string>());

    [TestMethod]
    public void Matrice_des_droits()
    {
        Assert.IsTrue(Membre.PeutLire("a") && Membre.PeutPiloter("a") && Membre.PeutDeployer("a"));
        Assert.IsFalse(Membre.PeutLire("b") || Membre.PeutPiloter("b") || Membre.PeutDeployer("b"));

        Assert.IsTrue(Support.PeutLire("b") && Support.PeutPiloter("b"));
        Assert.IsFalse(Support.PeutDeployer("b"));

        Assert.IsTrue(Admin.PeutLire("b") && Admin.PeutPiloter("b") && Admin.PeutDeployer("b"));

        Assert.IsFalse(Aucun.Autorise);
        Assert.IsTrue(Membre.Autorise && Support.Autorise && Admin.Autorise);
        Assert.IsFalse(Admin.PeutLire(null));
    }

    [TestMethod]
    public void Refus_introuvable_hors_equipe_et_interdit_sans_droit_de_deployer()
    {
        Assert.AreEqual(404, Assert.ThrowsExactly<ErreurPilotage>(() => Membre.ExigerLecture("b", "?")).StatutHttp);
        Assert.AreEqual(404, Assert.ThrowsExactly<ErreurPilotage>(() => Membre.ExigerDeploiement("b", "?")).StatutHttp);
        Assert.AreEqual(403, Assert.ThrowsExactly<ErreurPilotage>(() => Support.ExigerDeploiement("b", "?")).StatutHttp);
        Assert.AreEqual(403, Assert.ThrowsExactly<ErreurPilotage>(() => Support.ExigerAdmin()).StatutHttp);
        Admin.ExigerAdmin();
    }

    [TestMethod]
    public void Filtre_des_equipes()
    {
        Assert.IsNull(Admin.FiltreEquipes);
        Assert.IsNull(Support.FiltreEquipes);
        CollectionAssert.AreEquivalent(new[] { "a" }, Membre.FiltreEquipes!.ToArray());
    }

    [TestMethod]
    public void Ids_qualifies()
    {
        Assert.AreEqual("sgd.traitement.v2", IdsEquipe.Qualifier("sgd", "traitement.v2"));
        Assert.AreEqual("sgd", IdsEquipe.Equipe("sgd.traitement.v2"));
        Assert.AreEqual("traitement.v2", IdsEquipe.Local("sgd.traitement.v2"));
        Assert.AreEqual("sgd", IdsEquipe.Equipe("~sgd.traitement~0a1b2c3d"));
        Assert.AreEqual("sgd", IdsEquipe.Equipe("sgd.dossier-1:appel:3"));
        Assert.IsNull(IdsEquipe.Equipe("sans-equipe"));
        Assert.IsNull(IdsEquipe.Equipe(".x"));
    }

    [TestMethod]
    public void Ids_d_equipe_valides()
    {
        foreach (var id in new[] { "sgd", "equipe-2", "a" })
            Assert.IsTrue(IdsEquipe.EstIdEquipeValide(id), id);
        foreach (var id in new[] { "", "Sgd", "a.b", "-a", "a-", "admin", "accessibilite", new string('a', 51), "é" })
            Assert.IsFalse(IdsEquipe.EstIdEquipeValide(id), id);
    }
}
