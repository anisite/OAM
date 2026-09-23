using System.Text.Json.Nodes;
using OIM.Moteur.Definitions;
using OIM.Moteur.Expressions;
using OIM.Moteur.Pilotage;
using OIM.Moteur.Tests;

namespace OIM.Api;

public sealed record PaquetRequete(string Yaml, Dictionary<string, string>? Fichiers, string? Commentaire);

public sealed record ReponseValidation(bool Valide, IReadOnlyList<Diagnostic> Diagnostics, JsonNode? Definition);

public sealed record ReponseDeploiement(string Id, int Version, bool NouvelleVersion, IReadOnlyList<Diagnostic> Diagnostics, RapportTests? Tests);

public sealed record ReponseRefusTests(string Message, IReadOnlyList<Diagnostic> Diagnostics, RapportTests Tests);

public sealed record FichierDefinition(string Chemin, string Contenu);

public sealed record Activation(bool Actif);

public static class EndpointsDefinitions
{
    public static void MapDefinitions(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/definitions").WithTags("Définitions");

        g.MapGet("/", (ServiceDefinitions s, CancellationToken ct) => s.ListerAsync(ct));

        g.MapPost("/valider", (PaquetRequete r, ServiceDefinitions s) =>
        {
            var v = s.Valider(new PaquetDefinition(r.Yaml, r.Fichiers));
            return new ReponseValidation(v.Valide, v.Diagnostics, v.Brut);
        });

        // Déploiement : les tests métier du paquet (tests/*.yml) sont exécutés avant; en mode bloquant,
        // un échec refuse la version (422), sauf ?ignorerTests=true.
        g.MapPost("/", async (PaquetRequete r, bool? ignorerTests, ServiceDefinitions s, HttpContext http, CancellationToken ct) =>
            Deploiement(await s.DeployerAsync(new PaquetDefinition(r.Yaml, r.Fichiers), http.User.Utilisateur(), r.Commentaire, ct,
                ignorerTests: ignorerTests ?? false)));

        // Déploiement d'une archive zip (YAML du processus + gabarits + tests), ex. depuis un pipeline.
        g.MapPost("/zip", async (IFormFile fichier, string? commentaire, bool? ignorerTests, ServiceDefinitions s, HttpContext http, CancellationToken ct) =>
        {
            await using var flux = fichier.OpenReadStream();
            var paquet = PaquetDefinition.DepuisZip(flux);
            return Deploiement(await s.DeployerAsync(paquet, http.User.Utilisateur(), commentaire ?? fichier.FileName, ct,
                ignorerTests: ignorerTests ?? false));
        }).DisableAntiforgery();

        // Tests métier d'un paquet non déployé (concepteur, CI) — rien n'est enregistré.
        g.MapPost("/tests", async (PaquetRequete r, string? cas, bool? conserver, ServiceDefinitions s, CancellationToken ct) =>
            await s.TesterAsync(new PaquetDefinition(r.Yaml, r.Fichiers), cas, conserver ?? false, ct));

        // Tests métier d'une version déployée (courante par défaut).
        g.MapPost("/{id}/tests", async (string id, int? version, string? cas, bool? conserver, ServiceDefinitions s, CancellationToken ct) =>
            await s.TesterAsync(id, version, cas, conserver ?? false, ct));

        g.MapGet("/{id}", async (string id, int? version, ServiceDefinitions s, CancellationToken ct) =>
        {
            var v = await s.ObtenirAsync(id, version, ct);
            var lecture = LecteurDefinition.Lire(v.Yaml);
            return new
            {
                id = v.DefinitionId,
                v.Version,
                v.Yaml,
                fichiers = v.Fichiers.OrderBy(f => f.Key).Select(f => new FichierDefinition(f.Key, f.Value)),
                v.Empreinte,
                v.DeployePar,
                v.DeployeLe,
                v.Commentaire,
                definition = lecture.Brut,
                versions = await s.VersionsAsync(id, ct)
            };
        });

        g.MapGet("/{id}/zip", async (string id, int? version, ServiceDefinitions s, CancellationToken ct) =>
        {
            var v = await s.ObtenirAsync(id, version, ct);
            return Results.File(v.Paquet().VersZip("processus.yml"), "application/zip", $"{id}-v{v.Version}.zip");
        });

        g.MapPut("/{id}/actif", async (string id, Activation a, ServiceDefinitions s, CancellationToken ct) =>
        {
            await s.DefinirActifAsync(id, a.Actif, ct);
            return Results.NoContent();
        });

        api.MapGet("/catalogue", () => new
        {
            typesEtapes = CatalogueEtapes.Types,
            typesEntrees = CatalogueEtapes.TypesEntree,
            fonctions = Fonctions.Noms.Order(),
            variables = new[] { "entrees", "etapes", "evenement", "variables", "erreur", "instance", "maintenant" }
        }).WithTags("Définitions");
    }

    private static IResult Deploiement(ResultatDeploiementComplet r) =>
        r switch
        {
            { Deploiement: { } d } => Results.Ok(new ReponseDeploiement(d.Id, d.Version, d.NouvelleVersion, r.Validation.Diagnostics, r.Tests)),
            { RefuseParTests: true } => Results.UnprocessableEntity(new ReponseRefusTests(
                $"Déploiement refusé : {r.Tests!.Echoues} test(s) métier en échec.", r.Validation.Diagnostics, r.Tests)),
            _ => Results.BadRequest(new ReponseValidation(false, r.Validation.Diagnostics, r.Validation.Brut))
        };
}
