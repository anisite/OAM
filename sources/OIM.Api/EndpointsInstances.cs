using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OIM.Moteur.Definitions;
using OIM.Moteur.Expressions;
using OIM.Moteur.Hebergement;
using OIM.Moteur.Pilotage;

namespace OIM.Api;

public sealed record Raison(string? Texte);

public sealed record Redemarrage(bool VersionCourante);

public static class EndpointsInstances
{
    public static void MapInstances(this RouteGroupBuilder api)
    {
        // ── API cliente : démarrer, suivre, envoyer un événement ───────────────

        // Démarrage. Avec ?attendre=30s, l'appel est synchrone : il retourne la réponse publiée par une
        // étape « reponse » (ou la sortie finale), sinon 202 + URL de suivi à l'expiration du délai.
        api.MapPost("/processus/{processus}/instances", async (string processus, int? version, string? instanceId, string? attendre,
            HttpRequest requete, ServiceInstances s, IOptions<OptionsOim> options, HttpContext http, CancellationToken ct) =>
        {
            var debut = Stopwatch.GetTimestamp();
            var entrees = await LireCorpsAsync(requete, ct) switch
            {
                null => null,
                JsonObject o => o,
                _ => throw ErreurPilotage.Invalide("Le corps doit être un objet JSON contenant les entrées.")
            };
            var r = await s.DemarrerAsync(processus, entrees, version, instanceId, http.User.Utilisateur(), ct);

            if (LireAttente(attendre, options.Value) is not { } delai)
                return Results.Accepted(UrlInstance(r.InstanceId), r);
            return Repondre(await s.AttendreReponseAsync(r.InstanceId, delai, ct), http, debut);
        }).WithTags("Client");

        // Reprise de l'attente après un 202 (ex. délai trop court côté appelant).
        api.MapGet("/instances/{id}/reponse", async (string id, string? attendre, ServiceInstances s, IOptions<OptionsOim> options,
            HttpContext http, CancellationToken ct) =>
        {
            var debut = Stopwatch.GetTimestamp();
            return Repondre(await s.AttendreReponseAsync(id, LireAttente(attendre, options.Value) ?? TimeSpan.Zero, ct), http, debut);
        }).WithTags("Client");

        api.MapGet("/instances/{id}/statut", async (string id, ServiceInstances s) =>
        {
            var i = await s.ObtenirAsync(id);
            return new
            {
                i.InstanceId,
                i.Processus,
                i.Statut,
                termine = i.Terminee is not null,
                statutMetier = i.StatutPersonnalise?["statut"]?.GetValue<string>(),
                message = i.StatutPersonnalise?["message"]?.GetValue<string>(),
                evenementAttendu = i.StatutPersonnalise?["attente"]?["evenement"]?.GetValue<string>(),
                erreur = i.Erreur?.Message,
                i.Sortie
            };
        }).WithTags("Client");

        api.MapPost("/instances/{id}/evenements/{nom}", async (string id, string nom, HttpRequest requete, ServiceInstances s, CancellationToken ct) =>
        {
            await s.EnvoyerEvenementAsync(id, nom, await LireCorpsAsync(requete, ct));
            return Results.Accepted();
        }).WithTags("Client");

        // ── Suivi et pilotage ──────────────────────────────────────────────────

        var g = api.MapGroup("/instances").WithTags("Pilotage");

        g.MapGet("/", (RequetesSuivi q, string? statut, string? processus, string? recherche, DateTime? depuis, DateTime? jusqua,
                bool? attente, bool? sousProcessus, int? page, int? taille, CancellationToken ct) =>
            q.ListerAsync(new FiltreInstances(
                statut?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                processus, recherche, depuis, jusqua, attente ?? false, sousProcessus ?? true, page ?? 1, taille ?? 25), ct));

        g.MapGet("/{id}", (string id, ServiceInstances s) => s.ObtenirAsync(id));

        g.MapGet("/{id}/historique", (string id, RequetesSuivi q, CancellationToken ct) => q.HistoriqueAsync(id, ct));

        g.MapPost("/{id}/terminer", async (string id, Raison? r, ServiceInstances s) =>
        {
            await s.TerminerAsync(id, r?.Texte);
            return Results.Accepted();
        });

        g.MapPost("/{id}/suspendre", async (string id, Raison? r, ServiceInstances s) =>
        {
            await s.SuspendreAsync(id, r?.Texte);
            return Results.Accepted();
        });

        g.MapPost("/{id}/reprendre", async (string id, Raison? r, ServiceInstances s) =>
        {
            await s.ReprendreAsync(id, r?.Texte);
            return Results.Accepted();
        });

        g.MapPost("/{id}/relancer", async (string id, Raison? r, ServiceInstances s) =>
        {
            await s.RelancerAsync(id, r?.Texte);
            return Results.Accepted();
        });

        g.MapPost("/{id}/redemarrer", async (string id, Redemarrage? r, ServiceInstances s, HttpContext http) =>
            Results.Accepted(null, await s.RedemarrerAsync(id, r?.VersionCourante ?? false, http.User.Utilisateur())));

        g.MapDelete("/{id}", async (string id, ServiceInstances s) =>
        {
            await s.PurgerAsync(id);
            return Results.NoContent();
        });

        api.MapGet("/tableau-de-bord", (RequetesSuivi q, CancellationToken ct) => q.StatistiquesAsync(ct)).WithTags("Pilotage");
    }

    // Journal lisible : accents et apostrophes non échappés.
    private static readonly JsonSerializerOptions JsonJournal = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string UrlInstance(string id) => $"/api/instances/{Uri.EscapeDataString(id)}";

    private static TimeSpan? LireAttente(string? attendre, OptionsOim options)
    {
        if (string.IsNullOrWhiteSpace(attendre)) return null;
        if (!Duree.TryLire(attendre, out var delai) || delai < TimeSpan.Zero)
            throw ErreurPilotage.Invalide($"attendre « {attendre} » invalide (ex. 30s, 00:00:30).");
        return delai > options.AttenteSynchroneMax ? options.AttenteSynchroneMax : delai;
    }

    private static IResult Repondre(ResultatAttente r, HttpContext http, long debut)
    {
        var journal = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OIM.Api.ReponseSynchrone");
        var statutHttp = r.Nature switch
        {
            NatureAttente.Reponse => r.StatutHttp,
            NatureAttente.Terminee => 200,
            NatureAttente.Echec => 500,
            _ => 202
        };
        journal.Log(r.Nature == NatureAttente.Echec ? LogLevel.Warning : LogLevel.Information,
            "[{Instance}] Réponse synchrone à l'appelant : HTTP {StatutHttp} ({Nature}, instance {Statut}) après {Duree} ms {Corps}",
            r.InstanceId, statutHttp, r.Nature, r.Statut, (long)Stopwatch.GetElapsedTime(debut).TotalMilliseconds,
            r.Corps is null ? "" : Gabarit.Tronquer(r.Corps.ToJsonString(JsonJournal), 500));

        http.Response.Headers["X-Oim-Instance"] = r.InstanceId;
        http.Response.Headers.Location = UrlInstance(r.InstanceId);

        return r.Nature switch
        {
            // Corps publié par l'étape « reponse », tel quel, avec son statut HTTP.
            NatureAttente.Reponse => r.Corps is null
                ? Results.StatusCode(r.StatutHttp)
                : Results.Json(r.Corps, statusCode: r.StatutHttp),
            NatureAttente.Terminee => Results.Ok(new
            {
                r.InstanceId,
                r.Statut,
                statutMetier = r.StatutPersonnalise?["statut"]?.GetValue<string>(),
                message = r.StatutPersonnalise?["message"]?.GetValue<string>(),
                sortie = r.Corps
            }),
            NatureAttente.Echec => Results.Problem(title: "Le processus a échoué.", detail: Expression.EnTexte(r.Corps), statusCode: 500,
                extensions: new Dictionary<string, object?> { ["instanceId"] = r.InstanceId, ["statut"] = r.Statut }),
            _ => Results.Json(new
            {
                r.InstanceId,
                r.Statut,
                statutMetier = r.StatutPersonnalise?["statut"]?.GetValue<string>(),
                suivi = $"{UrlInstance(r.InstanceId)}/reponse?attendre=30s"
            }, statusCode: StatusCodes.Status202Accepted)
        };
    }

    private static async Task<JsonNode?> LireCorpsAsync(HttpRequest requete, CancellationToken ct)
    {
        using var lecteur = new StreamReader(requete.Body);
        var texte = await lecteur.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(texte)) return null;
        try
        {
            return JsonNode.Parse(texte);
        }
        catch (JsonException ex)
        {
            throw ErreurPilotage.Invalide($"JSON invalide : {ex.Message}");
        }
    }
}
