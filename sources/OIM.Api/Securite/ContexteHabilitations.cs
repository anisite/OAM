using System.Security.Claims;
using OIM.Moteur.Pilotage;

namespace OIM.Api.Securite;

/// <summary>
/// Calcule les <see cref="Habilitations"/> à partir des sujets : administrateurs et support (configuration),
/// équipes (membres enregistrés dans OIM). Le même calcul sert à toutes les requêtes et à l'émission du jeton.
/// </summary>
public sealed class FabriqueHabilitations(OptionsSecurite options, ServiceEquipes equipes)
{
    // Entrées vides ignorées : appsettings.json en garde une par tableau pour la substitution au déploiement.
    private readonly HashSet<string> _administrateurs = new(options.Administrateurs.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _support = new(options.Support.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);

    public async Task<Habilitations> CalculerAsync(string utilisateur, IReadOnlyCollection<string> sujets, CancellationToken ct = default)
    {
        if (!options.Active)
            return new Habilitations(Environment.UserName, true, false, new HashSet<string>());
        return new Habilitations(utilisateur,
            sujets.Any(_administrateurs.Contains),
            sujets.Any(_support.Contains),
            await equipes.EquipesDesSujetsAsync(sujets, ct));
    }

    /// <summary>Habilitations d'un jeton validé (null si la requête n'est pas authentifiée).</summary>
    public Task<Habilitations>? DepuisPrincipal(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!options.Active) return CalculerAsync(Environment.UserName, [], ct);
        if (principal.Identity is not { IsAuthenticated: true, Name: { } nom }) return null;
        return CalculerAsync(nom, principal.FindAll(Fournisseurs.TypeSujet).Select(c => c.Value).ToList(), ct);
    }

    /// <summary>Sujets utiles à écrire dans un jeton (admin, support, membres d'équipe) : borne sa taille.</summary>
    public async Task<IReadOnlySet<string>> SujetsPertinentsAsync(CancellationToken ct)
    {
        var pertinents = new HashSet<string>(await equipes.SujetsConnusAsync(ct), StringComparer.OrdinalIgnoreCase);
        pertinents.UnionWith(_administrateurs);
        pertinents.UnionWith(_support);
        return pertinents;
    }
}

/// <summary>Habilitations de la requête courante, injectables dans les endpoints (paramètre <see cref="Habilitations"/>).</summary>
public sealed class ContexteHabilitations
{
    public Habilitations? Valeur { get; set; }
}

public static class ExtensionsHabilitations
{
    public static void AjouterHabilitations(this IServiceCollection services, OptionsSecurite options)
    {
        services.AddSingleton(options);
        services.AddSingleton<FabriqueHabilitations>();
        services.AddScoped<ContexteHabilitations>();
        services.AddScoped(sp => sp.GetRequiredService<ContexteHabilitations>().Valeur
                                 ?? throw new InvalidOperationException("Habilitations non calculées pour cette requête."));
    }

    /// <summary>
    /// Après l'autorisation : calcule les habilitations de l'appelant de l'API et refuse (403) celui qui
    /// n'est ni administrateur, ni support, ni membre d'une équipe active.
    /// </summary>
    public static void UseHabilitations(this WebApplication app)
    {
        app.Use(async (http, suivant) =>
        {
            if (http.Request.Path.StartsWithSegments("/api") && !http.Request.Path.StartsWithSegments("/api/auth")
                && http.RequestServices.GetRequiredService<FabriqueHabilitations>().DepuisPrincipal(http.User, http.RequestAborted) is { } calcul)
            {
                var h = await calcul;
                if (!h.Autorise)
                {
                    await Results.Problem(title: $"« {h.Utilisateur} » n'est membre d'aucune équipe OIM.", statusCode: 403).ExecuteAsync(http);
                    return;
                }
                http.RequestServices.GetRequiredService<ContexteHabilitations>().Valeur = h;
            }
            await suivant(http);
        });
    }
}
