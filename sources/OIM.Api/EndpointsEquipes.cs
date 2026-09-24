using OIM.Moteur.Pilotage;

namespace OIM.Api;

public sealed record NouvelleEquipe(string Id, string Nom, string? Description, IReadOnlyList<string>? Membres);

public sealed record ModificationEquipe(string Nom, string? Description, bool Actif);

public sealed record Membres(IReadOnlyList<string> Sujets);

public static class EndpointsEquipes
{
    public static void MapEquipes(this RouteGroupBuilder api)
    {
        // Appelant courant : droits et équipes (interface).
        api.MapGet("/moi", (Habilitations h) => new
        {
            utilisateur = h.Utilisateur,
            admin = h.Admin,
            support = h.Support,
            equipes = h.Equipes.Order()
        }).WithTags("Équipes");

        var g = api.MapGroup("/equipes").WithTags("Équipes");

        // Ses équipes (toutes pour admin et support), avec les compteurs d'instances de l'accueil.
        g.MapGet("/", (Habilitations h, ServiceEquipes s, CancellationToken ct) => s.ListerAsync(h, ct));

        g.MapGet("/{id}", (string id, Habilitations h, ServiceEquipes s, CancellationToken ct) => s.ObtenirAsync(h, id, ct));

        g.MapPost("/", async (NouvelleEquipe e, Habilitations h, ServiceEquipes s, CancellationToken ct) =>
        {
            await s.CreerAsync(h, e.Id, e.Nom, e.Description, e.Membres, ct);
            return Results.Created($"/api/equipes/{e.Id}", await s.ObtenirAsync(h, e.Id, ct));
        });

        g.MapPut("/{id}", async (string id, ModificationEquipe e, Habilitations h, ServiceEquipes s, CancellationToken ct) =>
        {
            await s.ModifierAsync(h, id, e.Nom, e.Description, e.Actif, ct);
            return Results.NoContent();
        });

        g.MapPut("/{id}/membres", async (string id, Membres m, Habilitations h, ServiceEquipes s, CancellationToken ct) =>
        {
            await s.DefinirMembresAsync(h, id, m.Sujets, ct);
            return Results.NoContent();
        });

        g.MapDelete("/{id}", async (string id, Habilitations h, ServiceEquipes s, CancellationToken ct) =>
        {
            await s.SupprimerAsync(h, id, ct);
            return Results.NoContent();
        });
    }

    /// <summary>Équipe d'un déploiement : celle demandée, sinon l'unique équipe de l'appelant.</summary>
    public static string EquipeCible(this Habilitations h, string? equipe) =>
        !string.IsNullOrWhiteSpace(equipe) ? equipe
        : !h.VoitTout && h.Equipes.Count == 1 ? h.Equipes.First()
        : throw ErreurPilotage.Invalide("Précisez l'équipe (?equipe=).");
}
