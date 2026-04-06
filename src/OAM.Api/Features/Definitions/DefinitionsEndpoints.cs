using System.IO.Compression;
using OAM.Domain.Entities;
using OAM.Domain.Interfaces;
using OAM.Workflow.Core.Connecteurs;
using OAM.Workflow.Core.Yaml;

namespace OAM.Api.Features.Definitions;

public static class DefinitionsEndpoints
{
    public static IEndpointRouteBuilder MapDefinitions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/definitions").RequireAuthorization();

        group.MapGet("/", async (IDefinitionWorkflowRepository repo, string? equipe) =>
        {
            var liste = await repo.ListerAsync(equipe);
            return Results.Ok(liste.Select(ToDto));
        });

        group.MapGet("/{id:guid}", async (IDefinitionWorkflowRepository repo, Guid id) =>
        {
            var def = await repo.ObtenirParIdAsync(id);
            return def is null ? Results.NotFound() : Results.Ok(ToDto(def));
        });

        group.MapGet("/{id:guid}/yaml", async (IDefinitionWorkflowRepository repo, Guid id) =>
        {
            var def = await repo.ObtenirParIdAsync(id);
            return def is null ? Results.NotFound() : Results.Content(def.ContenuYaml, "text/plain; charset=utf-8");
        });

        group.MapGet("/{id:guid}/versions", async (IDefinitionWorkflowRepository repo, Guid id) =>
        {
            var versions = await repo.ListerVersionsAsync(id);
            return Results.Ok(versions.Select(v =>
                new VersionDefinitionDto(v.Id, v.HashVersion, v.DateChargement, v.DeployePar)));
        });

        // Déploiement via zip : chaque dossier = un workflow
        // Structure : NomWorkflow/workflow.yml, extensions.yml, http-clients.yml
        group.MapPost("/deployer", async (
            HttpRequest req,
            IDefinitionWorkflowRepository repo,
            ConfigurationYamlHttp configHttp,
            HttpContext ctx) =>
        {
            if (!req.HasFormContentType)
                return Results.BadRequest(new { erreur = "Multipart form-data requis (champ 'fichier')" });

            var form = await req.ReadFormAsync();
            var fichier = form.Files.GetFile("fichier");
            if (fichier is null)
                return Results.BadRequest(new { erreur = "Fichier zip manquant (champ 'fichier')" });

            var deployePar = ctx.User.Identity?.Name;
            var resultats = new List<DefinitionWorkflowDto>();
            var erreurs = new List<string>();

            using var zip = new ZipArchive(fichier.OpenReadStream(), ZipArchiveMode.Read);

            // Normaliser les séparateurs (Windows crée des zip avec \)
            // Grouper les entrées par dossier de premier niveau
            var parWorkflow = zip.Entries
                .Select(e => (Entry: e, Path: e.FullName.Replace('\\', '/')))
                .Where(x => !x.Path.EndsWith('/') && x.Path.Contains('/'))
                .GroupBy(x => x.Path.Split('/')[0], x => x.Entry);

            foreach (var groupe in parWorkflow)
            {
                var nomDossier = groupe.Key;
                try
                {
                    var workflowEntry = groupe.FirstOrDefault(e => e.Name == "workflow.yml");
                    if (workflowEntry is null)
                    {
                        erreurs.Add($"{nomDossier}: workflow.yml manquant");
                        continue;
                    }

                    var contenuWorkflow = LireEntree(workflowEntry);
                    var contenuExtensions = LireEntreeOptionnelle(groupe, "extensions.yml");
                    var contenuHttpClients = LireEntreeOptionnelle(groupe, "http-clients.yml");

                    // Valider le YAML
                    DefinitionWorkflowYaml parsed;
                    try { parsed = YamlParser.ParseDefinition(contenuWorkflow); }
                    catch (Exception ex)
                    {
                        erreurs.Add($"{nomDossier}: YAML invalide — {ex.Message}");
                        continue;
                    }

                    var hash = YamlParser.CalculerHashCombine(contenuWorkflow, contenuExtensions, contenuHttpClients);

                    var definition = new DefinitionWorkflow
                    {
                        Nom = parsed.Nom ?? nomDossier,
                        Description = parsed.Description,
                        Equipe = parsed.Equipe,
                        ContenuYaml = contenuWorkflow,
                        ContenuExtensions = contenuExtensions,
                        ContenuHttpClients = contenuHttpClients,
                        HashVersion = hash,
                        DeployePar = deployePar
                    };

                    var resultat = await repo.CreerOuMettreAJourAsync(definition);

                    // Charger les configs HTTP en mémoire si présentes
                    if (contenuHttpClients is not null)
                        configHttp.ChargerDepuisYaml(contenuHttpClients, resultat.Nom);

                    resultats.Add(ToDto(resultat));
                }
                catch (Exception ex)
                {
                    erreurs.Add($"{nomDossier}: {ex.Message}");
                }
            }

            return Results.Ok(new { deployes = resultats, erreurs });
        }).DisableAntiforgery();

        return app;
    }

    private static string LireEntree(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? LireEntreeOptionnelle(IGrouping<string, ZipArchiveEntry> groupe, string nomFichier)
    {
        var entry = groupe.FirstOrDefault(e => e.Name == nomFichier);
        return entry is null ? null : LireEntree(entry);
    }

    private static DefinitionWorkflowDto ToDto(DefinitionWorkflow d) => new(
        d.Id, d.Nom, d.Description, d.Equipe, d.HashVersion,
        d.DateCreation, d.DateModification, d.Actif);
}
