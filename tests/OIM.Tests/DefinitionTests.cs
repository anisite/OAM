using System.Text.Json.Nodes;
using OIM.Moteur.Definitions;

namespace OIM.Tests;

[TestClass]
public sealed class DefinitionTests
{
    internal static string DossierExemple =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "definitions", "traitement-demande"));

    internal static PaquetDefinition PaquetExemple() => PaquetDefinition.DepuisDossier(DossierExemple);

    [TestMethod]
    public void Lecture_de_l_exemple()
    {
        var r = LecteurDefinition.Lire(PaquetExemple().Yaml);
        Assert.IsTrue(r.Valide, string.Join("\n", r.Diagnostics));
        var def = r.Definition!;

        Assert.AreEqual("traitement-demande", def.Id);
        Assert.AreEqual(2, def.Entrees.Count);
        Assert.AreEqual("int", def.Entrees[0].Type);
        Assert.IsTrue(def.Entrees[0].Requis);

        var valider = def.TrouverEtape("valider")!;
        Assert.AreEqual(new PolitiqueReprise(3, TimeSpan.FromSeconds(30), 2, null), valider.Retry);
        Assert.AreEqual("accuser", valider.Suivant.Single().Aller);
        Assert.AreEqual("reponse", def.TrouverEtape("accuser")!.Type);
        Assert.AreEqual("valider-dossier.yml", valider.Texte("requete"));

        var approbation = def.TrouverEtape("approbation")!;
        Assert.AreEqual("5.00:00:00", approbation.Texte("delai"));
        Assert.AreEqual(2, approbation.Suivant.Count);
        Assert.AreEqual("{{ evenement.approuve == true }}", approbation.Suivant[0].Condition);
        Assert.IsNull(approbation.Suivant[1].Condition);
        Assert.AreEqual("refuser", approbation.Suivant[1].Aller);

        Assert.IsTrue(def.TrouverEtape("confirmer")!.Fin);
    }

    [TestMethod]
    public void Validation_complete_de_l_exemple()
    {
        var r = ValidateurDefinition.Valider(PaquetExemple());
        Assert.IsTrue(r.Valide, string.Join("\n", r.Diagnostics));
        Assert.AreEqual(0, r.Diagnostics.Count, string.Join("\n", r.Diagnostics));
    }

    [TestMethod]
    public void Paquet_detecte_le_yaml_principal_et_les_annexes()
    {
        var p = PaquetExemple();
        StringAssert.Contains(p.Yaml, "id: traitement-demande");
        Assert.IsNotNull(p.TrouverRequete("valider-dossier.yml"));
        Assert.IsNotNull(p.TrouverGabarit("confirmation"));
        Assert.AreEqual(p.Empreinte(), PaquetExemple().Empreinte());
    }

    [TestMethod]
    public void Validation_signale_les_erreurs()
    {
        const string yaml = """
            id: mauvais
            entrees:
              x: { type: entier }
            etapes:
              - id: a
                type: http
                suivant: inexistante
              - id: a
                type: inconnu
              - id: b
                type: attendreEvenement
                evenement: e
                delai: bientot
                suivant:
                  - sinon: a
                  - si: "{{ evenement.ok == }}"
                    aller: a
              - id: c
                type: courriel
                gabarit: absent
                message: "{{ etapes.zzz.sortie }}"
                fin: true
            """;
        var r = ValidateurDefinition.Valider(new PaquetDefinition(yaml));
        var messages = string.Join("\n", r.Erreurs.Select(e => e.Message));

        Assert.IsFalse(r.Valide);
        StringAssert.Contains(messages, "type « entier » inconnu");
        StringAssert.Contains(messages, "« requete » est obligatoire");
        StringAssert.Contains(messages, "étape inexistante « inexistante »");
        StringAssert.Contains(messages, "« a » est utilisé 2 fois");
        StringAssert.Contains(messages, "Type d'étape « inconnu » inconnu");
        StringAssert.Contains(messages, "Délai « bientot » invalide");
        StringAssert.Contains(messages, "« sinon » doit être la dernière");
        StringAssert.Contains(messages, "Expression invalide");
        StringAssert.Contains(messages, "Gabarit de courriel « absent » introuvable");
        StringAssert.Contains(messages, "étape inexistante « zzz »");
    }

    [TestMethod]
    public void Yaml_invalide_donne_la_ligne()
    {
        var r = LecteurDefinition.Lire("id: x\netapes:\n  - id: a\n   type: http");
        Assert.IsFalse(r.Valide);
        Assert.IsNotNull(r.Diagnostics[0].Ligne);
    }

    [TestMethod]
    [DataRow("00:00:30", 30d)]
    [DataRow("5.00:00:00", 432000d)]
    [DataRow("30s", 30d)]
    [DataRow("15m", 900d)]
    [DataRow("2h", 7200d)]
    [DataRow("5j", 432000d)]
    [DataRow("P5D", 432000d)]
    [DataRow("PT1M30S", 90d)]
    public void Durees(string texte, double secondes) =>
        Assert.AreEqual(TimeSpan.FromSeconds(secondes), Duree.Lire(texte, "test"));

    [TestMethod]
    public void Entrees_normalisees()
    {
        var def = LecteurDefinition.Lire("""
            id: e
            entrees:
              n: { type: int, requis: true }
              actif: { type: bool, defaut: true }
              montant: number
            etapes:
              - { id: a, type: decision, suivant: [ { sinon: a } ] }
            """).Definition!;

        var (ok, erreurs) = ValidateurEntrees.Normaliser(def, JsonNode.Parse("""{ "n": "42", "montant": "3,5", "extra": 1 }""")!.AsObject());
        Assert.AreEqual(0, erreurs.Count);
        Assert.AreEqual(42L, ok["n"]!.GetValue<long>());
        Assert.IsTrue(ok["actif"]!.GetValue<bool>());
        Assert.AreEqual(3.5, ok["montant"]!.GetValue<double>());
        Assert.AreEqual(1, ok["extra"]!.GetValue<int>());

        var (_, erreurs2) = ValidateurEntrees.Normaliser(def, new JsonObject { ["n"] = "x" });
        Assert.AreEqual(1, erreurs2.Count);
    }
}
