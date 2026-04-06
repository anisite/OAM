using System.Text;
using System.Text.Json;
using OAM.Domain.Enums;
using OAM.Domain.Interfaces;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OAM.Api.Features.Instances;

public static class InstancesEndpoints
{
    public static IEndpointRouteBuilder MapInstances(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/instances").RequireAuthorization();

        group.MapGet("/", async (IInstanceWorkflowRepository repo, EtatWorkflow? etat, Guid? definitionId) =>
            Results.Ok((await repo.ListerAsync(etat, definitionId)).Select(ToDto)));

        group.MapGet("/{id:guid}", async (IInstanceWorkflowRepository repo, Guid id) =>
        {
            var instance = await repo.ObtenirParIdAsync(id);
            return instance is null ? Results.NotFound() : Results.Ok(ToDto(instance));
        });

        group.MapGet("/erreurs", async (IInstanceWorkflowRepository repo, Guid? definitionId) =>
            Results.Ok((await repo.ListerEnErreurAsync(definitionId)).Select(ToDto)));

        group.MapPost("/demarrer", async (IMoteurWorkflow moteur, DemarrerWorkflowDto dto) =>
        {
            var instance = await moteur.DemarrerAsync(dto.DefinitionId, dto.DonneesEntree, dto.CorrelationId);
            return Results.Ok(ToDto(instance));
        });

        group.MapPost("/{id:guid}/reprendre", async (IMoteurWorkflow moteur, Guid id) =>
        {
            await moteur.ReprendreAsync(id);
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/reprendre-tache", async (IMoteurWorkflow moteur, Guid id, ReprendreTacheDto dto) =>
        {
            await moteur.ReprendreTacheAsync(id, dto.NomTache, dto.DonneesEntreeCorrigees);
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/pauser", async (IMoteurWorkflow moteur, Guid id) =>
        {
            await moteur.PauserAsync(id);
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/annuler", async (IMoteurWorkflow moteur, Guid id) =>
        {
            await moteur.AnnulerAsync(id);
            return Results.Ok();
        });

        group.MapPatch("/{id:guid}/taches", async (
            IInstanceWorkflowRepository instanceRepo,
            IExecutionTacheRepository tacheRepo,
            Guid id, PatchTachesDto dto) =>
        {
            var instance = await instanceRepo.ObtenirParIdAsync(id);
            if (instance is null) return Results.NotFound();

            foreach (var patch in dto.Taches)
            {
                var tache = instance.Taches.FirstOrDefault(t => t.Id == patch.TacheId);
                if (tache is null) continue;
                if (patch.DonneesEntreeCorrigees is not null)
                    tache.DonneesEntree = patch.DonneesEntreeCorrigees;
                tache.Etat = EtatTache.EnAttente;
                tache.Erreur = null;
            }

            await tacheRepo.MettreAJourEnLotAsync(instance.Taches.ToList());
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/hook/{nomTache}", async (IMoteurWorkflow moteur, Guid id, string nomTache, object? donnees) =>
        {
            await moteur.ReprendreTacheAsync(id, nomTache, donnees?.ToString());
            return Results.Ok();
        }).AllowAnonymous();

        // Exporter une instance comme fichier de test .md
        group.MapGet("/{id:guid}/exporter-test", async (IInstanceWorkflowRepository repo, HttpContext ctx, Guid id) =>
        {
            var instance = await repo.ObtenirParIdAsync(id);
            if (instance is null) return Results.NotFound();

            var nomWorkflow = instance.DefinitionWorkflow?.Nom ?? "workflow";
            var md = new StringBuilder();
            md.AppendLine($"# {nomWorkflow}");
            md.AppendLine();

            // INPUT
            md.AppendLine("### INPUT");
            md.AppendLine("```yaml");
            md.AppendLine(JsonVersYaml(instance.DonneesEntree ?? "{}").TrimEnd());
            md.AppendLine("```");
            md.AppendLine();

            // MOCKS — une entrée par tâche http qui a réussi
            var tachesHttp = instance.Taches
                .Where(t => t.TypeConnecteur == "http" && t.DonneesSortie is not null)
                .OrderBy(t => t.Ordre)
                .ToList();

            if (tachesHttp.Count > 0)
            {
                md.AppendLine("### Mocks");
                md.AppendLine();
                foreach (var tache in tachesHttp)
                {
                    md.AppendLine($"#### {tache.NomTache}");
                    md.AppendLine("```yaml");
                    md.AppendLine(JsonVersYaml(tache.DonneesSortie!).TrimEnd());
                    md.AppendLine("```");
                    md.AppendLine();
                }
            }

            // OUTPUT attendu
            md.AppendLine("### OUTPUT attendu");
            md.AppendLine("```yaml");
            md.AppendLine($"etat: {instance.Etat}");
            md.AppendLine("taches:");
            foreach (var tache in instance.Taches.OrderBy(t => t.Ordre))
            {
                md.AppendLine($"  {tache.NomTache}:");
                md.AppendLine($"    etat: {tache.Etat}");

                // Inclure les inputs métier (exclure les clés internes)
                if (tache.DonneesEntree is not null)
                {
                    var entree = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tache.DonneesEntree);
                    var clesInternes = new HashSet<string>(["httpClientId", "mock", "expression"], StringComparer.OrdinalIgnoreCase);
                    var inputMetier = entree?
                        .Where(kv => !clesInternes.Contains(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

                    if (inputMetier is { Count: > 0 })
                    {
                        md.AppendLine("    input:");
                        foreach (var (cle, val) in inputMetier)
                            md.AppendLine($"      {cle}: \"{val}\"");
                    }
                }
            }
            md.Append("```");

            var nomFichier = $"{nomWorkflow.ToLowerInvariant().Replace(' ', '-')}.md";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{nomFichier}\"";
            return Results.Content(md.ToString(), "text/plain; charset=utf-8");
        });

        return app;
    }

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static string JsonVersYaml(string json)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return YamlSerializer.Serialize(JsonElementVersObjet(element));
    }

    private static object? JsonElementVersObjet(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementVersObjet(p.Value)),
        JsonValueKind.Array  => e.EnumerateArray().Select(JsonElementVersObjet).ToList(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? (object?)l : e.GetDouble(),
        JsonValueKind.True   => (object?)true,
        JsonValueKind.False  => false,
        _                    => null
    };

    internal static InstanceWorkflowDto ToDto(Domain.Entities.InstanceWorkflow i) => new(
        i.Id, i.DefinitionWorkflowId, i.DefinitionWorkflow?.Nom ?? "",
        i.CorrelationId, i.HashVersionConfig, i.Etat,
        i.DateCreation, i.DateDebut, i.DateFin, i.Erreur,
        i.Taches.Select(t => new ExecutionTacheDto(
            t.Id, t.NomTache, t.TypeConnecteur, t.Ordre, t.Etat,
            t.DonneesEntree, t.DonneesSortie, t.Erreur,
            t.DateDebut, t.DateFin)).ToList());
}
