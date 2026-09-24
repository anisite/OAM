using System.Text.Json.Nodes;
using OIM.Moteur.Definitions;
using OIM.Moteur.Expressions;
using OIM.Moteur.Orchestration.Activites;

namespace OIM.Tests;

/// <summary>Fonctions et règles ajoutées pour la transmission de documents (reprise d'ECS25A).</summary>
[TestClass]
public sealed class TransmissionTests
{
    private static readonly JsonObject Contexte = JsonNode.Parse("""
        { "entrees": { "champs": { "form_adulte1Prenom": "Émilie-Anne", "form_adulte1Nas1": "123 456-789", "form_adulte1Sexe": "Feminin",
                                   "form_TypeDemande_0": "Afdr", "form_adulte1Adresse_0_CodePostal": "G9A 1A1" } },
          "variables": { "estAFDR": true, "estEQ": false, "cle": "074" } }
        """)!.AsObject();

    private static string Texte(string expr) => Expression.EnTexte(Expression.Analyser(expr).Evaluer(Contexte));

    [TestMethod]
    [DataRow("formaterNom(entrees.champs.form_adulte1Prenom)", "EMILIE-ANNE")]
    [DataRow("formaterNAS(entrees.champs.form_adulte1Nas1)", "123456789")]
    [DataRow("sousChaine(entrees.champs.form_adulte1Sexe, 0, 1)", "F")]
    [DataRow("sousChaine('abc', 1)", "bc")]
    [DataRow("sousChaine('abc', 5, 2)", "")]
    [DataRow("sousChaine('abc', 2, 10)", "c")]
    [DataRow("remplacer(entrees.champs.form_adulte1Adresse_0_CodePostal, ' ', '')", "G9A1A1")]
    [DataRow("si(variables.estAFDR, 'Oui', 'Non')", "Oui")]
    [DataRow("si(variables.estEQ, 'Oui', 'Non')", "Non")]
    [DataRow("si(variables.estAFDR && variables.estEQ, 'DD', si(variables.estAFDR, 'SR', 'EQ'))", "SR")]
    [DataRow("si(variables.estAFDR, 'AFDR', '') + si(variables.estEQ, ' et SP Emploi', '')", "AFDR")]
    public void Fonctions_ecs(string expression, string attendu) => Assert.AreEqual(attendu, Texte(expression), expression);

    [TestMethod]
    public void Condition_de_liste_par_contient() =>
        Assert.IsTrue(Expression.EstVrai(Expression.Analyser("contient('032 074 076', variables.cle)").Evaluer(Contexte)));

    [TestMethod]
    public void Variantes_par_palier_et_langue()
    {
        var parPalier = JsonNode.Parse("""{ "unitaire": "u@x", "production": "p@x" }""");
        Assert.AreEqual("u@x", Expression.EnTexte(Variantes.Choisir(parPalier, "unitaire")));
        Assert.AreEqual("p@x", Expression.EnTexte(Variantes.Choisir(parPalier, "production")));
        Assert.IsNull(Variantes.Choisir(parPalier, "techno"));

        var parLangue = JsonNode.Parse("""{ "fr": "Bonjour", "en": "Hello" }""");
        Assert.AreEqual("Hello", Expression.EnTexte(Variantes.Choisir(parLangue, "unitaire", "en")));
        Assert.AreEqual("tout", Expression.EnTexte(Variantes.Choisir(JsonNode.Parse("""{ "tous": "tout" }"""), "production")));
        Assert.AreEqual("simple", Expression.EnTexte(Variantes.Choisir(JsonValue.Create("simple"), "production")));
    }

    [TestMethod]
    public void Validation_de_la_boite_generique()
    {
        var yaml = """
            id: essai
            etapes:
              - id: boite
                type: boiteGenerique
                gabarit: g
                blocs:
                  - si: "{{ true }}"
                  - bsq: { table: absente.yml, cle: "001" }
                fin: true
            """;
        var r = ValidateurDefinition.Valider(new PaquetDefinition(yaml, new Dictionary<string, string>
        {
            ["gabarits/g.yml"] = "sujet: s\ncorps: c"
        }));
        var messages = r.Erreurs.Select(e => e.Message).ToList();
        Assert.IsTrue(messages.Any(m => m.Contains("Bloc 1") && m.Contains("« a »")), string.Join("\n", messages));
        Assert.IsTrue(messages.Any(m => m.Contains("Bloc 2") && m.Contains("absente.yml")), string.Join("\n", messages));
        // Le destinataire vient de la boîte retenue : pas d'erreur « aucun destinataire » sur le gabarit.
        Assert.IsFalse(messages.Any(m => m.Contains("destinataire")), string.Join("\n", messages));
    }
}
