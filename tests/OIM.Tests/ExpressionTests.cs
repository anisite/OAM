using System.Text.Json.Nodes;
using OIM.Moteur.Expressions;

namespace OIM.Tests;

[TestClass]
public sealed class ExpressionTests
{
    private static readonly JsonObject Contexte = JsonNode.Parse("""
        {
          "entrees": { "dossierId": 123, "courriel": "a@b.c", "montant": "12.5", "vide": "" },
          "etapes": { "valider": { "sortie": { "numero": "D-42", "items": [1, 2, 3] } },
                      "valider-dossier": { "sortie": { "ok": true } } },
          "evenement": { "approuve": true, "motif": null },
          "variables": {}
        }
        """)!.AsObject();

    private static JsonNode? Eval(string expr) => Expression.Analyser(expr).Evaluer(Contexte);

    [TestMethod]
    [DataRow("evenement.approuve == true", true)]
    [DataRow("evenement.approuve", true)]
    [DataRow("evenement.approuve == 'true'", true)]
    [DataRow("!evenement.approuve", false)]
    [DataRow("non evenement.approuve", false)]
    [DataRow("entrees.dossierId == 123", true)]
    [DataRow("entrees.dossierId == '123'", true)]
    [DataRow("entrees.dossierId > 100 && entrees.dossierId <= 123", true)]
    [DataRow("entrees.dossierId > 100 et entrees.dossierId < 123", false)]
    [DataRow("entrees.montant >= 12", true)]
    [DataRow("evenement.motif == null", true)]
    [DataRow("evenement.inexistant == null", true)]
    [DataRow("etapes.valider-dossier.sortie.ok", true)]
    [DataRow("contient(etapes.valider.sortie.items, 2)", true)]
    [DataRow("vide(entrees.vide) ou faux", true)]
    [DataRow("longueur(etapes.valider.sortie.items) == 3", true)]
    [DataRow("etapes.valider.sortie.items[-1] == 3", true)]
    public void Conditions(string expression, bool attendu) =>
        Assert.AreEqual(attendu, Expression.EstVrai(Eval(expression)), expression);

    [TestMethod]
    public void Arithmetique_et_concatenation()
    {
        Assert.AreEqual(246L, Eval("entrees.dossierId * 2")!.GetValue<long>());
        Assert.AreEqual(122L, Eval("entrees.dossierId - 1")!.GetValue<long>());
        Assert.AreEqual("D-42/123", Eval("etapes.valider.sortie.numero + '/' + entrees.dossierId")!.GetValue<string>());
        Assert.AreEqual("défaut", Eval("evenement.motif || 'défaut'")!.GetValue<string>());
        Assert.AreEqual("oui", Eval("evenement.approuve ? 'oui' : 'non'")!.GetValue<string>());
    }

    [TestMethod]
    public void Gabarit_expression_unique_conserve_le_type()
    {
        var resolu = Gabarit.Resoudre(JsonNode.Parse("""{ "id": "{{ entrees.dossierId }}", "liste": ["{{ evenement.approuve }}"] }"""), Contexte)!;
        Assert.AreEqual(123, resolu["id"]!.GetValue<int>());
        Assert.IsTrue(resolu["liste"]![0]!.GetValue<bool>());
    }

    [TestMethod]
    public void Gabarit_interpolation()
    {
        Assert.AreEqual("Confirmation #D-42",
            Gabarit.Interpoler("Confirmation #{{ etapes.valider.sortie.numero }}", Contexte));
        Assert.AreEqual("Dossier 123 (a@b.c)",
            Gabarit.Interpoler("Dossier {{entrees.dossierId}} ({{ entrees.courriel }})", Contexte));
    }

    [TestMethod]
    public void Condition_accepte_les_accolades() =>
        Assert.IsTrue(Gabarit.Condition("{{ evenement.approuve == true }}", Contexte));

    [TestMethod]
    [DataRow("entrees.")]
    [DataRow("(a == 1")]
    [DataRow("a === b")]
    [DataRow("inconnue(1)")]
    [DataRow("'non terminé")]
    public void Erreurs_de_syntaxe(string expression) =>
        Assert.ThrowsExactly<ErreurExpression>(() => Expression.Analyser(expression));

    [TestMethod]
    public void Chemins_references()
    {
        var chemins = Expression.Analyser("etapes.valider.sortie.numero == entrees.x").Chemins()
            .Select(c => string.Join('.', c)).ToList();
        CollectionAssert.AreEquivalent(new[] { "etapes.valider.sortie.numero", "entrees.x" }, chemins);
    }
}
