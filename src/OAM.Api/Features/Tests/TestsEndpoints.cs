using System.IO.Compression;
using System.Text.Json;
using OAM.Domain.Entities;
using OAM.Domain.Interfaces;
using OAM.Workflow.Core.Engine;
using OAM.Workflow.Core.Yaml;

namespace OAM.Api.Features.Tests;

public static class TestsEndpoints
{
    public static IEndpointRouteBuilder MapTests(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tests").RequireAuthorization();

        group.MapGet("/{definitionId:guid}", async (ICasTestRepository repo, Guid definitionId) =>
        {
            var cas = await repo.ListerParDefinitionAsync(definitionId);
            return Results.Ok(cas.Select(c => new CasTestDto(c.Id, c.Nom, c.DateChargement, c.DeployePar)));
        });

        group.MapGet("/{definitionId:guid}/{nom}", async (ICasTestRepository repo, Guid definitionId, string nom) =>
        {
            var cas = await repo.ObtenirAsync(definitionId, nom);
            return cas is null ? Results.NotFound() : Results.Content(cas.Contenu, "text/plain; charset=utf-8");
        });

        // Exécuter un cas de test : parse le markdown, enregistre les mocks, démarre le workflow
        group.MapPost("/{definitionId:guid}/{nom}/executer", async (
            ICasTestRepository casRepo,
            IInstanceWorkflowRepository instanceRepo,
            IMoteurWorkflow moteur,
            GestionnaireMock gestionnaireMock,
            Guid definitionId,
            string nom) =>
        {
            var cas = await casRepo.ObtenirAsync(definitionId, nom);
            if (cas is null) return Results.NotFound();

            TestCaseResult parsed;
            try { parsed = TestCaseParser.Analyser(cas.Contenu); }
            catch (Exception ex)
            {
                return Results.BadRequest(new { erreur = $"Impossible d'analyser le cas de test : {ex.Message}" });
            }

            var correlationId = $"test-{nom}-{Guid.NewGuid():N}";

            // Enregistrer les mocks scopés à ce correlationId
            foreach (var (id, reponseJson) in parsed.Mocks)
                gestionnaireMock.AjouterMock(id, correlationId, null, reponseJson);

            OAM.Domain.Entities.InstanceWorkflow instance;
            try
            {
                // Exécution synchrone pour les tests : on bypass la queue
                instance = await moteur.DemarrerAsync(definitionId, parsed.InputJson, correlationId);
                await moteur.ExecuterAsync(instance.Id);
                instance = await instanceRepo.ObtenirParIdAsync(instance.Id) ?? instance;
            }
            finally
            {
                foreach (var (id, _) in parsed.Mocks)
                    gestionnaireMock.RetirerMock(id, correlationId);
            }

            // Valider le OUTPUT attendu si défini
            var differences = new List<string>();
            if (parsed.OutputAttendu is { } attendu)
            {
                if (attendu.Etat is not null &&
                    !string.Equals(instance.Etat.ToString(), attendu.Etat, StringComparison.OrdinalIgnoreCase))
                    differences.Add($"etat: attendu={attendu.Etat}, obtenu={instance.Etat}");

                foreach (var (nomTache, tacheAttendue) in attendu.Taches)
                {
                    var tacheReelle = instance.Taches.FirstOrDefault(t =>
                        string.Equals(t.NomTache, nomTache, StringComparison.OrdinalIgnoreCase));

                    if (tacheReelle is null)
                    {
                        differences.Add($"tache.{nomTache}: introuvable dans l'instance");
                        continue;
                    }

                    if (tacheAttendue.Etat is not null &&
                        !string.Equals(tacheReelle.Etat.ToString(), tacheAttendue.Etat, StringComparison.OrdinalIgnoreCase))
                        differences.Add($"tache.{nomTache}.etat: attendu={tacheAttendue.Etat}, obtenu={tacheReelle.Etat}");

                    if (tacheAttendue.Input is { Count: > 0 } inputAttendu)
                    {
                        var donneesEntree = tacheReelle.DonneesEntree is not null
                            ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tacheReelle.DonneesEntree)
                            : null;

                        foreach (var (cle, valeurAttendue) in inputAttendu)
                        {
                            var valeurReelle = donneesEntree is not null &&
                                              donneesEntree.TryGetValue(cle, out var elem)
                                ? elem.ToString()
                                : null;

                            if (!string.Equals(valeurReelle, valeurAttendue, StringComparison.Ordinal))
                                differences.Add($"tache.{nomTache}.input.{cle}: attendu={valeurAttendue ?? "null"}, obtenu={valeurReelle ?? "null"}");
                        }
                    }
                }
            }

            return Results.Ok(new
            {
                instanceId = instance.Id,
                correlationId = instance.CorrelationId,
                passe = differences.Count == 0,
                differences
            });
        });

        // Déploiement via zip : NomWorkflow/tests/nom-scenario.md
        group.MapPost("/deployer", async (
            HttpRequest req,
            IDefinitionWorkflowRepository defRepo,
            ICasTestRepository casRepo,
            HttpContext ctx) =>
        {
            if (!req.HasFormContentType)
                return Results.BadRequest(new { erreur = "Multipart form-data requis (champ 'fichier')" });

            var form = await req.ReadFormAsync();
            var fichier = form.Files.GetFile("fichier");
            if (fichier is null)
                return Results.BadRequest(new { erreur = "Fichier zip manquant (champ 'fichier')" });

            var deployePar = ctx.User.Identity?.Name;
            var resultats = new List<object>();
            var erreurs = new List<string>();

            using var zip = new ZipArchive(fichier.OpenReadStream(), ZipArchiveMode.Read);

            // Normaliser les séparateurs (Windows crée des zip avec \)
            // Grouper les entrées tests par workflow (NomWorkflow/tests/*.md)
            var parWorkflow = zip.Entries
                .Select(e => (Entry: e, Path: e.FullName.Replace('\\', '/')))
                .Where(x => !x.Path.EndsWith('/') && x.Path.Contains('/'))
                .GroupBy(x => x.Path.Split('/')[0], x => x.Entry);

            foreach (var groupe in parWorkflow)
            {
                var nomWorkflow = groupe.Key;

                var testEntries = groupe
                    .Where(e =>
                    {
                        var parties = e.FullName.Replace('\\', '/').Split('/');
                        return parties.Length >= 3 && parties[1].Equals("tests", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                if (testEntries.Count == 0)
                    continue;

                try
                {
                    var definition = await defRepo.ObtenirParNomAsync(nomWorkflow);
                    if (definition is null)
                    {
                        erreurs.Add($"{nomWorkflow}: définition introuvable — déployez d'abord le workflow");
                        continue;
                    }

                    var casTests = testEntries.Select(e =>
                    {
                        using var stream = e.Open();
                        using var reader = new StreamReader(stream);
                        return new CasTest
                        {
                            DefinitionWorkflowId = definition.Id,
                            Nom = Path.GetFileNameWithoutExtension(e.Name),
                            Contenu = reader.ReadToEnd(),
                            DeployePar = deployePar
                        };
                    }).ToList();

                    await casRepo.CreerOuMettreAJourEnLotAsync(casTests);
                    resultats.Add(new { workflow = nomWorkflow, tests = casTests.Select(c => c.Nom) });
                }
                catch (Exception ex)
                {
                    erreurs.Add($"{nomWorkflow}: {ex.Message}");
                }
            }

            return Results.Ok(new { deployes = resultats, erreurs });
        }).DisableAntiforgery();

        return app;
    }
}
