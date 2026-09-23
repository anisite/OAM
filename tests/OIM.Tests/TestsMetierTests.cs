using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OIM.Moteur.Definitions;
using OIM.Moteur.Hebergement;
using OIM.Moteur.Pilotage;
using OIM.Moteur.Stockage;
using OIM.Moteur.Tests;

namespace OIM.Tests;

/// <summary>
/// Tests métier des paquets de <c>definitions/</c> : chaque fichier <c>tests/*.yml</c> devient un test
/// MSTest (pipeline CI). Exécutés sur le vrai moteur (LocalDB « OIM », base OIM_Tests, task hub dédié).
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class TestsMetierTests
{
    private const string Connexion = @"Server=(localdb)\OIM;Database=OIM_Tests;Integrated Security=True;TrustServerCertificate=True";
    private static IHost? _hote;

    private static string DossierDefinitions =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "definitions"));

    private static ServiceTests Tests => _hote!.Services.GetRequiredService<ServiceTests>();

    [ClassInitialize]
    public static async Task Initialiser(TestContext _)
    {
        try
        {
            await using var cn = new SqlConnection(Connexion.Replace("Database=OIM_Tests", "Database=master"));
            await cn.OpenAsync();
        }
        catch (SqlException)
        {
            return;
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Oim"] = Connexion,
            ["Oim:TaskHub"] = "tests-metier",
            ["Logging:LogLevel:Default"] = "Warning"
        });
        builder.Services.AjouterOim(builder.Configuration);
        _hote = builder.Build();
        await _hote.StartAsync();
    }

    [ClassCleanup]
    public static async Task Nettoyer()
    {
        if (_hote is null) return;
        await _hote.StopAsync();
        _hote.Dispose();
    }

    [TestInitialize]
    public void VerifierHote()
    {
        if (_hote is null) Assert.Inconclusive("LocalDB « OIM » indisponible : sqllocaldb create OIM -s");
    }

    /// <summary>Un test par cas : « traitement-demande / tests/approbation.yml ».</summary>
    public static IEnumerable<object[]> CasDesPaquets() =>
        Directory.EnumerateDirectories(DossierDefinitions)
            .SelectMany(d => CasTest.Trouver(PaquetDefinition.DepuisDossier(d)).Select(c => new object[] { Path.GetFileName(d), c.Chemin }));

    [TestMethod]
    [DynamicData(nameof(CasDesPaquets))]
    public async Task Cas_metier(string processus, string cas)
    {
        var rapport = await Tests.ExecuterAsync(PaquetDefinition.DepuisDossier(Path.Combine(DossierDefinitions, processus)), cas);

        Assert.AreEqual(0, rapport.Diagnostics.Count, string.Join("\n", rapport.Diagnostics));
        var resultat = rapport.Cas.Single();
        Assert.IsTrue(resultat.Reussi, $"{resultat.Nom} :\n  " + string.Join("\n  ", resultat.Ecarts));
    }

    [TestMethod]
    public async Task Un_ecart_est_detecte_et_explique()
    {
        var paquet = PaquetAvecCas("""
            nom: Attendu volontairement faux
            entrees: { dossierId: 1, courriel: a@b.c }
            mocks:
              valider: { statut: 200, corps: { numero: D-1 } }
            scenario:
              - attendre: approbation
                evenement: { decision: { approuve: true } }
            attendu:
              statut: Completed
              etapeFinale: refuser
              message: "Confirmation #D-1"
              courriels: [ { a: [autre@b.c] } ]
            """);

        var r = (await Tests.ExecuterAsync(paquet)).Cas.Single();

        Assert.IsFalse(r.Reussi);
        CollectionAssert.AreEquivalent(new[] { "etapeFinale", "courriels[0].a[0]" },
            r.Ecarts.Select(e => e.Split(' ')[0]).ToArray(), string.Join("\n", r.Ecarts));
    }

    [TestMethod]
    public async Task Scenario_incoherent_avec_le_parcours()
    {
        var paquet = PaquetAvecCas("""
            nom: Le service échoue avant l'attente
            entrees: { dossierId: 2, courriel: a@b.c }
            mocks:
              valider: { statut: 500 }
            scenario:
              - attendre: approbation
                evenement: { decision: { approuve: true } }
            attendu: { statut: Completed }
            """);

        var r = (await Tests.ExecuterAsync(paquet)).Cas.Single();

        Assert.IsFalse(r.Reussi);
        StringAssert.Contains(r.Ecarts[0], "Failed avant d'atteindre « approbation »");
    }

    [TestMethod]
    public async Task Deploiement_refuse_si_un_test_echoue_sauf_ignorerTests()
    {
        var id = $"refus-tests-{Guid.NewGuid():N}"[..30];
        var exemple = PaquetDefinition.DepuisDossier(Path.Combine(DossierDefinitions, "traitement-demande"));
        var fichiers = exemple.Fichiers.Where(f => !CasTest.EstCasTest(f.Key)).ToDictionary();
        fichiers["tests/faux.yml"] = """
            nom: Faux
            entrees: { dossierId: 3, courriel: a@b.c }
            mocks: { valider: { statut: 200, corps: { numero: D-3 } } }
            attendu: { statut: Completed }
            """;
        var paquet = new PaquetDefinition(exemple.Yaml.Replace("id: traitement-demande", $"id: {id}"), fichiers);
        var definitions = _hote!.Services.GetRequiredService<ServiceDefinitions>();

        var refuse = await definitions.DeployerAsync(paquet, "tests", null);
        Assert.IsTrue(refuse.RefuseParTests);
        Assert.IsNull(await _hote.Services.GetRequiredService<IDepotDefinitions>().ObtenirAsync(id));

        var force = await definitions.DeployerAsync(paquet, "tests", null, ignorerTests: true);
        Assert.IsNotNull(force.Deploiement);
        Assert.IsFalse(force.Tests!.Reussi);
    }

    [TestMethod]
    public async Task Les_tests_ne_laissent_aucune_trace_au_suivi()
    {
        await Tests.ExecuterAsync(PaquetDefinition.DepuisDossier(Path.Combine(DossierDefinitions, "traitement-demande")), "en-attente");

        var suivi = _hote!.Services.GetRequiredService<RequetesSuivi>();
        Assert.AreEqual(0, (await suivi.ListerAsync(new FiltreInstances(Recherche: "~test"))).Total);
        Assert.IsFalse((await suivi.StatistiquesAsync()).ParProcessus.Any(p => p.Processus.StartsWith('~')));
    }

    private static PaquetDefinition PaquetAvecCas(string cas)
    {
        var exemple = PaquetDefinition.DepuisDossier(Path.Combine(DossierDefinitions, "traitement-demande"));
        var fichiers = exemple.Fichiers.Where(f => !CasTest.EstCasTest(f.Key)).ToDictionary();
        fichiers["tests/cas.yml"] = cas;
        return new PaquetDefinition(exemple.Yaml, fichiers);
    }
}
