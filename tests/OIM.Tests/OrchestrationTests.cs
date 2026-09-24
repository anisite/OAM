using System.Text.Json.Nodes;
using DurableTask.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OIM.Moteur.Definitions;
using OIM.Moteur.Hebergement;
using OIM.Moteur.Pilotage;

namespace OIM.Tests;

/// <summary>
/// Tests d'intégration de bout en bout : vrai worker DurableTask sur SQL Server (LocalDB « OIM »,
/// base OIM_Tests). Ignorés (Inconclusive) si l'instance LocalDB n'est pas disponible.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class OrchestrationTests
{
    private const string Connexion = BaseDeTests.Connexion;
    private static IHost? _hote;
    private static string _dossierCourriels = string.Empty;

    /// <summary>Équipe des processus de test; <see cref="H"/> : habilitations du moteur (toutes les équipes).</summary>
    private const string Eq = "essais";
    private static readonly Habilitations H = Habilitations.Systeme;

    private static string Q(string id) => IdsEquipe.Qualifier(Eq, id);

    private static ServiceInstances Instances => _hote!.Services.GetRequiredService<ServiceInstances>();
    private static TaskHubClient Client => _hote!.Services.GetRequiredService<TaskHubClient>();

    [ClassInitialize]
    public static async Task Initialiser(TestContext _)
    {
        if (!await BaseDeTests.AssurerAsync()) return;

        _dossierCourriels = Path.Combine(Path.GetTempPath(), "oim-tests-courriels", Guid.NewGuid().ToString("N"));
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Oim"] = Connexion,
            ["Oim:TaskHub"] = "tests",
            ["Oim:Courriel:DossierDepot"] = _dossierCourriels,
            ["Logging:LogLevel:Default"] = "Warning"
        });
        builder.Services.AjouterOim(builder.Configuration);
        _hote = builder.Build();
        await _hote.StartAsync();

        var definitions = _hote.Services.GetRequiredService<ServiceDefinitions>();
        var equipes = _hote.Services.GetRequiredService<ServiceEquipes>();
        await equipes.AssurerAsync(Eq);
        await equipes.AssurerAsync("autre");
        foreach (var paquet in PaquetsDeTest())
        {
            var r = await definitions.DeployerAsync(H, Eq, paquet, null);
            Assert.IsNotNull(r.Deploiement, string.Join("\n", r.Validation.Diagnostics));
        }

        // Même id de processus dans une autre équipe, avec un autre résultat : le sous-processus du
        // parent doit être celui de sa propre équipe.
        var piege = await definitions.DeployerAsync(H, "autre", new PaquetDefinition("""
            id: enfant
            entrees:
              n: { type: int, requis: true }
            etapes:
              - id: calcul
                type: definir
                variables: { double: "{{ entrees.n * 100 }}" }
                fin: true
            """), null);
        Assert.AreEqual("autre.enfant", piege.Deploiement!.Id);
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

    [TestMethod]
    public async Task Approbation_confirme_et_retourne_le_message()
    {
        var id = await DemarrerAsync("td-test", new() { ["dossierId"] = 7, ["courriel"] = "x@y.z" });
        await AttendreEvenementAsync(id, "decision");

        await Instances.EnvoyerEvenementAsync(H, id, "decision", JsonNode.Parse("""{ "approuve": true }"""));
        var fin = await AttendreFinAsync(id);

        Assert.AreEqual(OrchestrationStatus.Completed, fin.OrchestrationStatus);
        var sortie = JsonNode.Parse(fin.Output)!;
        Assert.AreEqual("confirmer", sortie["etapeFinale"]!.GetValue<string>());
        Assert.AreEqual("Confirmation #D-2026-00042", sortie["message"]!.GetValue<string>());
        Assert.AreEqual("Demande approuvée", sortie["statut"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Refus()
    {
        var id = await DemarrerAsync("td-test", new() { ["dossierId"] = 8, ["courriel"] = "x@y.z" });
        await AttendreEvenementAsync(id, "decision");

        await Instances.EnvoyerEvenementAsync(H, id, "decision", JsonNode.Parse("""{ "approuve": false, "motif": "incomplet" }"""));
        var fin = await AttendreFinAsync(id);

        Assert.AreEqual("refuser", JsonNode.Parse(fin.Output)!["etapeFinale"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Delai_expire_declenche_la_relance_puis_revient_en_attente()
    {
        var id = await DemarrerAsync("td-test", new() { ["dossierId"] = 9, ["courriel"] = "x@y.z" });

        // délai de 2 s dans la définition de test : on attend au moins une relance
        await AttendreAsync(id, e => (JsonNode.Parse(e.Status ?? "{}")?["transitions"]?.GetValue<int>() ?? 0) >= 4, TimeSpan.FromSeconds(30));
        await AttendreEvenementAsync(id, "decision");

        var parcours = JsonNode.Parse((await Client.GetOrchestrationStateAsync(id)).Status)!["parcours"]!.AsArray()
            .Select(x => x!.GetValue<string>()).ToList();
        CollectionAssert.IsSubsetOf(new[] { "valider", "approbation", "relance" }, parcours);
        Assert.IsTrue(Directory.GetFiles(_dossierCourriels, "*.eml").Any(f => File.ReadAllText(f).Contains("approbateurs@exemple.gouv.qc.ca")));

        await Instances.EnvoyerEvenementAsync(H, id, "decision", JsonNode.Parse("""{ "approuve": true }"""));
        Assert.AreEqual(OrchestrationStatus.Completed, (await AttendreFinAsync(id)).OrchestrationStatus);
    }

    [TestMethod]
    public async Task Evenement_recu_avant_l_attente_est_conserve()
    {
        var id = await DemarrerAsync("attente-tardive", new());
        await Instances.EnvoyerEvenementAsync(H, id, "go", JsonNode.Parse("""{ "valeur": 5 }"""));

        var fin = await AttendreFinAsync(id);
        Assert.AreEqual(OrchestrationStatus.Completed, fin.OrchestrationStatus);
        Assert.AreEqual("Valeur 5", JsonNode.Parse(fin.Output)!["message"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Erreur_http_apres_reprises_suit_siErreur()
    {
        var id = await DemarrerAsync("http-erreur", new());
        var fin = await AttendreFinAsync(id, TimeSpan.FromSeconds(60));

        Assert.AreEqual(OrchestrationStatus.Completed, fin.OrchestrationStatus);
        var sortie = JsonNode.Parse(fin.Output)!;
        Assert.AreEqual("compenser", sortie["etapeFinale"]!.GetValue<string>());
        StringAssert.Contains(sortie["variables"]!["cause"]!.GetValue<string>(), "HTTP 503");
    }

    [TestMethod]
    public async Task Erreur_sans_siErreur_fait_echouer_l_instance()
    {
        var id = await DemarrerAsync("http-echec", new());
        var fin = await AttendreFinAsync(id, TimeSpan.FromSeconds(60));

        Assert.AreEqual(OrchestrationStatus.Failed, fin.OrchestrationStatus);
        StringAssert.Contains(fin.FailureDetails!.ErrorMessage, "Étape « appel »");
    }

    [TestMethod]
    public async Task Sous_processus()
    {
        var id = await DemarrerAsync("parent", new() { ["n"] = 21 });
        var fin = await AttendreFinAsync(id);

        Assert.AreEqual(OrchestrationStatus.Completed, fin.OrchestrationStatus, fin.FailureDetails?.ErrorMessage);
        Assert.AreEqual("Résultat 42", JsonNode.Parse(fin.Output)!["message"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Suspension_et_reprise()
    {
        var id = await DemarrerAsync("td-test", new() { ["dossierId"] = 10, ["courriel"] = "x@y.z" });
        await AttendreEvenementAsync(id, "decision");

        await Instances.SuspendreAsync(H, id, "test");
        await AttendreAsync(id, e => e.OrchestrationStatus == OrchestrationStatus.Suspended, TimeSpan.FromSeconds(20));

        await Instances.ReprendreAsync(H, id, "test");
        await AttendreAsync(id, e => e.OrchestrationStatus == OrchestrationStatus.Running, TimeSpan.FromSeconds(20));

        await Instances.TerminerAsync(H, id, "fin du test");
        Assert.AreEqual(OrchestrationStatus.Terminated, (await AttendreFinAsync(id)).OrchestrationStatus);
    }

    [TestMethod]
    public async Task Demarrage_synchrone_recoit_la_reponse_pendant_que_le_processus_continue()
    {
        var id = await DemarrerAsync("td-test", new() { ["dossierId"] = 11, ["courriel"] = "x@y.z" });
        var r = await Instances.AttendreReponseAsync(H, id, TimeSpan.FromSeconds(20));

        Assert.AreEqual(NatureAttente.Reponse, r.Nature);
        Assert.AreEqual(201, r.StatutHttp);
        Assert.AreEqual("D-2026-00042", r.Corps!["numero"]!.GetValue<string>());

        // Le processus poursuit : il attend maintenant la décision.
        await AttendreEvenementAsync(id, "decision");
        await Instances.TerminerAsync(H, id, "fin du test");
    }

    [TestMethod]
    public async Task Etape_reponse_tracee_dans_l_historique_seulement_pour_le_moteur_v2()
    {
        var nouvelle = await DemarrerAsync("td-test", new() { ["dossierId"] = 12, ["courriel"] = "x@y.z" });

        // Instance « ancienne » : entrée sans version de moteur, comme avant son introduction.
        var ancienne = Q($"td-test-v1-{Guid.NewGuid():N}");
        var version = (await _hote!.Services.GetRequiredService<ServiceDefinitions>().ObtenirAsync(H, Q("td-test"), null)).Version;
        await Client.CreateOrchestrationInstanceAsync(Q("td-test"), version.ToString(), ancienne,
            new Newtonsoft.Json.Linq.JRaw($$"""{ "definitionId": "{{Q("td-test")}}", "version": {{version}}, "entrees": { "dossierId": 13, "courriel": "x@y.z" } }"""));

        foreach (var id in new[] { nouvelle, ancienne }) await AttendreEvenementAsync(id, "decision");

        var suivi = _hote.Services.GetRequiredService<RequetesSuivi>();
        var historique = await suivi.HistoriqueAsync(H, nouvelle);
        var planifiee = historique.Single(h => h.Type == "TaskScheduled" && h.Nom == "oim.reponse");
        var terminee = historique.Single(h => h.Type == "TaskCompleted" && h.TacheId == planifiee.TacheId);
        // Planification : gabarit non évalué + contexte reçu; résultat : réponse produite.
        StringAssert.Contains(planifiee.Donnees, "contexte");
        StringAssert.Contains(planifiee.Donnees, "etapes.valider.sortie.numero");
        StringAssert.Contains(terminee.Donnees, "D-2026-00042");
        Assert.IsFalse(terminee.Donnees!.Contains("contexte"));
        Assert.IsFalse((await suivi.HistoriqueAsync(H, ancienne)).Any(h => h.Nom == "oim.reponse"));

        // Les deux se terminent normalement (aucune erreur de non-déterminisme à la relecture).
        foreach (var id in new[] { nouvelle, ancienne })
        {
            await Instances.EnvoyerEvenementAsync(H, id, "decision", JsonNode.Parse("""{ "approuve": true }"""));
            Assert.AreEqual(OrchestrationStatus.Completed, (await AttendreFinAsync(id)).OrchestrationStatus);
        }
    }

    [TestMethod]
    public async Task Demarrage_synchrone_sans_etape_reponse_retourne_la_sortie_finale()
    {
        var id = await DemarrerAsync("enfant", new() { ["n"] = 4 });
        var r = await Instances.AttendreReponseAsync(H, id, TimeSpan.FromSeconds(20));

        Assert.AreEqual(NatureAttente.Terminee, r.Nature);
        Assert.AreEqual(8L, r.Corps!["variables"]!["double"]!.GetValue<long>());
    }

    [TestMethod]
    public async Task Demarrage_synchrone_delai_depasse()
    {
        var id = await DemarrerAsync("attente-tardive", new());
        var r = await Instances.AttendreReponseAsync(H, id, TimeSpan.FromMilliseconds(300));

        Assert.AreEqual(NatureAttente.DelaiDepasse, r.Nature);
        await Instances.TerminerAsync(H, id, "fin du test");
    }

    [TestMethod]
    public async Task Entrees_invalides_sont_refusees()
    {
        var ex = await Assert.ThrowsExactlyAsync<ErreurPilotage>(() =>
            Instances.DemarrerAsync(H, Q("td-test"), new JsonObject { ["dossierId"] = "abc" }));
        Assert.AreEqual(400, ex.StatutHttp);
        Assert.AreEqual(2, ex.Details.Count);
    }

    [TestMethod]
    public async Task Une_equipe_ne_voit_ni_les_processus_ni_les_instances_d_une_autre()
    {
        var definitions = _hote!.Services.GetRequiredService<ServiceDefinitions>();
        var suivi = _hote.Services.GetRequiredService<RequetesSuivi>();
        var alice = new Habilitations("alice", false, false, new HashSet<string> { Eq });
        var bob = new Habilitations("bob", false, false, new HashSet<string> { "autre" });
        var support = new Habilitations("support", false, true, new HashSet<string>());

        var id = (await Instances.DemarrerAsync(alice, Q("attente-tardive"), new JsonObject(), instanceId: "dossier-1")).InstanceId;
        Assert.AreEqual(Q("dossier-1"), id);

        // Bob (autre équipe) : tout est « introuvable ».
        async Task Introuvable(Func<Task> action) =>
            Assert.AreEqual(404, (await Assert.ThrowsExactlyAsync<ErreurPilotage>(action)).StatutHttp);
        await Introuvable(() => Instances.ObtenirAsync(bob, id));
        await Introuvable(() => Instances.TerminerAsync(bob, id, null));
        await Introuvable(() => Instances.EnvoyerEvenementAsync(bob, id, "go", null));
        await Introuvable(() => suivi.HistoriqueAsync(bob, id));
        await Introuvable(() => Instances.DemarrerAsync(bob, Q("enfant"), new JsonObject { ["n"] = 1 }));
        await Introuvable(() => definitions.ObtenirAsync(bob, Q("enfant"), null));
        await Introuvable(() => suivi.ListerAsync(bob, new FiltreInstances(Equipe: Eq)));
        Assert.IsFalse((await suivi.ListerAsync(bob, new FiltreInstances(Recherche: "dossier-1"))).Elements.Any(i => i.InstanceId == id));
        CollectionAssert.AreEqual(new[] { "autre" }, (await definitions.ListerAsync(bob)).Select(d => d.Equipe).Distinct().ToArray());

        // Le même instanceId dans l'autre équipe ne crée aucun conflit.
        var deBob = (await Instances.DemarrerAsync(bob, "autre.enfant", new JsonObject { ["n"] = 1 }, instanceId: "dossier-1")).InstanceId;
        Assert.AreEqual("autre.dossier-1", deBob);

        // Alice (son équipe) voit son instance.
        Assert.IsTrue((await suivi.ListerAsync(alice, new FiltreInstances(Recherche: "dossier-1"))).Elements.Select(i => i.InstanceId).SequenceEqual([id]));
        Assert.IsTrue((await definitions.ListerAsync(alice)).All(d => d.Equipe == Eq));

        // Support : voit et pilote toutes les équipes, sans déployer.
        Assert.AreEqual(2, (await suivi.ListerAsync(support, new FiltreInstances(Recherche: "dossier-1"))).Total);
        var refus = await Assert.ThrowsExactlyAsync<ErreurPilotage>(() => definitions.DeployerAsync(support, Eq, PaquetsDeTest().First(), null));
        Assert.AreEqual(403, refus.StatutHttp);
        await Instances.TerminerAsync(support, id, "fin du test");
    }

    // ── Outils ───────────────────────────────────────────────────────────────

    private static async Task<string> DemarrerAsync(string processus, JsonObject entrees) =>
        (await Instances.DemarrerAsync(H, Q(processus), entrees, instanceId: $"{processus}-{Guid.NewGuid():N}")).InstanceId;

    private static Task<OrchestrationState> AttendreEvenementAsync(string id, string evenement) =>
        AttendreAsync(id, e => e.OrchestrationStatus == OrchestrationStatus.Running
                               && JsonNode.Parse(e.Status ?? "{}")?["attente"]?["evenement"]?.GetValue<string>() == evenement,
            TimeSpan.FromSeconds(30));

    private static Task<OrchestrationState> AttendreFinAsync(string id, TimeSpan? delai = null) =>
        AttendreAsync(id, e => e.OrchestrationStatus is OrchestrationStatus.Completed or OrchestrationStatus.Failed or OrchestrationStatus.Terminated,
            delai ?? TimeSpan.FromSeconds(30));

    private static async Task<OrchestrationState> AttendreAsync(string id, Func<OrchestrationState, bool> condition, TimeSpan delai)
    {
        var limite = DateTime.UtcNow + delai;
        OrchestrationState? etat = null;
        while (DateTime.UtcNow < limite)
        {
            etat = await Client.GetOrchestrationStateAsync(id);
            if (etat is not null && condition(etat)) return etat;
            await Task.Delay(250);
        }
        Assert.Fail($"Délai dépassé pour {id} : {etat?.OrchestrationStatus} {etat?.Status} {etat?.FailureDetails?.ErrorMessage}");
        return null!;
    }

    private static IEnumerable<PaquetDefinition> PaquetsDeTest()
    {
        var exemple = DefinitionTests.PaquetExemple();
        yield return new PaquetDefinition(
            exemple.Yaml.Replace("id: traitement-demande", "id: td-test")
                .Replace("5.00:00:00", "00:00:02")
                .Replace("delai: 00:00:30", "delai: 00:00:01"),
            exemple.Fichiers.ToDictionary());

        yield return new PaquetDefinition("""
            id: attente-tardive
            etapes:
              - id: pause
                type: delai
                duree: 2s
                suivant: attendre
              - id: attendre
                type: attendreEvenement
                evenement: go
                message: "Valeur {{ evenement.valeur }}"
                fin: true
            """);

        const string mock503 = """
            http_client:
              service:
                method: GET
                url: https://exemple.invalid/service
                mock:
                  enabled: true
                  status_code: 503
                  body: indisponible
            """;

        yield return new PaquetDefinition("""
            id: http-erreur
            etapes:
              - id: appel
                type: http
                requete: service.yml
                retry: { tentatives: 2, delai: 1s }
                siErreur: compenser
                suivant: fin
              - id: compenser
                type: definir
                variables: { cause: "{{ erreur.message }}" }
                fin: true
              - id: fin
                type: decision
                suivant: [ { sinon: compenser } ]
            """, new Dictionary<string, string> { ["service.yml"] = mock503 });

        yield return new PaquetDefinition("""
            id: http-echec
            etapes:
              - id: appel
                type: http
                requete: service.yml
                fin: true
            """, new Dictionary<string, string> { ["service.yml"] = mock503 });

        yield return new PaquetDefinition("""
            id: enfant
            entrees:
              n: { type: int, requis: true }
            etapes:
              - id: calcul
                type: definir
                variables: { double: "{{ entrees.n * 2 }}" }
                fin: true
            """);

        yield return new PaquetDefinition("""
            id: parent
            entrees:
              n: int
            etapes:
              - id: appel
                type: sousProcessus
                processus: enfant
                entrees: { n: "{{ entrees.n }}" }
                message: "Résultat {{ etapes.appel.sortie.variables.double }}"
                fin: true
            """);
    }
}
