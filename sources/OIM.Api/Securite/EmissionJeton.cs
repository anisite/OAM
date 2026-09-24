using System.Security.Claims;
using System.Security.Principal;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace OIM.Api.Securite;

public sealed record JetonEmis(string? Jeton, DateTime? Expiration, string Utilisateur);

/// <summary>
/// Fournisseur Interne : <c>GET /api/auth/jeton</c> échange l'authentification Windows contre un jeton
/// Bearer OIM (nom du compte AD et, en groupes, ceux de ses groupes AD qui servent à OIM).
/// </summary>
public static class EmissionJeton
{
    public static void MapAuth(this IEndpointRouteBuilder app, OptionsSecurite options, IWebHostEnvironment env)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Authentification");

        var interne = options.Fournisseurs.Values.FirstOrDefault(f => f.Type == TypeFournisseur.Interne);
        if (!options.Active || interne is null)
        {
            // Sécurité désactivée (ou jetons émis ailleurs) : l'interface fonctionne sans en-tête.
            auth.MapGet("/jeton", () => new JetonEmis(null, null, Environment.UserName));
            if (env.IsDevelopment()) auth.MapGet("/jeton-dev", () => new JetonEmis(null, null, Environment.UserName));
            return;
        }

        auth.MapGet("/jeton", async (HttpContext http, FabriqueHabilitations fabrique) =>
            await EmettreAsync(interne, fabrique, http.User.Identity!.Name!, GroupesWindows(http.User.Identity), http.RequestAborted))
            .RequireAuthorization(Fournisseurs.PolitiqueWindows);

        // Développement : le proxy Vite ne relaie pas la négociation Windows (liée à la connexion).
        if (env.IsDevelopment())
            auth.MapGet("/jeton-dev", async (FabriqueHabilitations fabrique, CancellationToken ct) =>
            {
                using var courant = WindowsIdentity.GetCurrent();
                return await EmettreAsync(interne, fabrique, courant.Name, GroupesWindows(courant), ct);
            });
    }

    private static async Task<IResult> EmettreAsync(OptionsFournisseur f, FabriqueHabilitations fabrique, string utilisateur,
        IEnumerable<string> groupes, CancellationToken ct)
    {
        var pertinents = await fabrique.SujetsPertinentsAsync(ct);
        var roles = groupes.Where(pertinents.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var h = await fabrique.CalculerAsync(utilisateur, [.. roles.Prepend(utilisateur).Select(s => f.Prefixe + s)], ct);
        if (!h.Autorise)
            return Results.Problem(title: $"« {utilisateur} » n'est membre d'aucune équipe OIM.", statusCode: 403);

        var expiration = DateTime.UtcNow.AddMinutes(f.DureeMinutes);
        var claims = new Dictionary<string, object>
        {
            [f.ClaimNom] = utilisateur,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
        };
        if (roles.Count > 0) claims[f.ClaimGroupes] = roles.ToArray();

        var jeton = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = f.Emetteur,
            Audience = f.Audience,
            Claims = claims,
            Expires = expiration,
            SigningCredentials = new SigningCredentials(Fournisseurs.CleSignature(f.Cle!), SecurityAlgorithms.HmacSha256)
        });
        return Results.Ok(new JetonEmis(jeton, expiration, utilisateur));
    }

    /// <summary>
    /// Groupes AD en noms « DOMAINE\Groupe » : l'identité Windows ne porte que des SID,
    /// sur lesquels IsInRole(nom) ne s'applique pas.
    /// </summary>
    private static List<string> GroupesWindows(IIdentity? identite)
    {
        var noms = new List<string>();
        if (identite is WindowsIdentity { Groups: { } groupes })
            foreach (var sid in groupes)
                try { noms.Add(sid.Translate(typeof(NTAccount)).Value); }
                catch (IdentityNotMappedException) { }
        return noms;
    }
}
