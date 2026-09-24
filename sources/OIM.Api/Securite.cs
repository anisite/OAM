using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace OIM.Api;

/// <summary>Section « Oim:Securite ».</summary>
public sealed class OptionsSecurite
{
    public const string Section = "Oim:Securite";

    public bool Active { get; set; } = true;

    /// <summary>Groupes AD autorisés (vide = tout utilisateur authentifié).</summary>
    public string[] Groupes { get; set; } = [];

    public OptionsJeton Jeton { get; set; } = new();
}

public sealed class OptionsJeton
{
    /// <summary>Clé de signature HS256, au moins 32 caractères; identique sur tous les serveurs.</summary>
    public string? Cle { get; set; }

    public string Emetteur { get; set; } = "OIM";
    public string Audience { get; set; } = "OIM";
    public int DureeMinutes { get; set; } = 480;
}

public sealed record JetonEmis(string? Jeton, DateTime? Expiration, string Utilisateur, IReadOnlyList<string> Roles);

/// <summary>
/// Authentification : l'API exige un jeton Bearer (JWT signé par OIM). Le jeton s'obtient par
/// authentification Windows sur <c>GET /api/auth/jeton</c> : nom du compte AD et, en rôles, les
/// groupes configurés dont l'utilisateur est membre.
/// </summary>
public static class Securite
{
    public const string Politique = "Oim";
    public const string PolitiqueWindows = "OimWindows";

    private const string ClaimNom = "name";
    private const string ClaimRole = "role";

    /// <summary>Utilisateur courant, ou compte local si la sécurité est désactivée.</summary>
    public static string Utilisateur(this ClaimsPrincipal? principal) =>
        principal?.Identity is { IsAuthenticated: true, Name: { } nom } ? nom : Environment.UserName;

    public static void AjouterSecurite(this IServiceCollection services, OptionsSecurite options)
    {
        var cle = CleSignature(options.Jeton);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Jeton.Emetteur,
                    ValidAudience = options.Jeton.Audience,
                    IssuerSigningKey = cle,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = ClaimNom,
                    RoleClaimType = ClaimRole,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            })
            .AddNegotiate();

        services.AddAuthorization(o =>
        {
            o.AddPolicy(Politique, p =>
            {
                p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser();
                if (options.Groupes.Length > 0) p.RequireRole(options.Groupes);
            });
            o.AddPolicy(PolitiqueWindows, p =>
                p.AddAuthenticationSchemes(NegotiateDefaults.AuthenticationScheme).RequireAuthenticatedUser());
        });
    }

    public static void MapAuth(this IEndpointRouteBuilder app, OptionsSecurite options, IWebHostEnvironment env)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Authentification");

        if (!options.Active)
        {
            // Sécurité désactivée : aucun jeton requis; l'interface fonctionne sans en-tête.
            auth.MapGet("/jeton", () => new JetonEmis(null, null, Environment.UserName, []));
            return;
        }

        var cle = CleSignature(options.Jeton);

        auth.MapGet("/jeton", (HttpContext http) =>
        {
            var utilisateur = http.User.Identity!.Name!;
            var groupes = GroupesWindows(http.User);
            var roles = options.Groupes.Where(groupes.Contains).ToList();
            if (options.Groupes.Length > 0 && roles.Count == 0)
                return Results.Problem(title: $"« {utilisateur} » n'est membre d'aucun groupe autorisé.", statusCode: 403);
            return Results.Ok(Emettre(options.Jeton, cle, utilisateur, roles));
        }).RequireAuthorization(PolitiqueWindows);

        // Développement : le proxy Vite ne relaie pas la négociation Windows (liée à la connexion).
        if (env.IsDevelopment())
            auth.MapGet("/jeton-dev", () => Emettre(options.Jeton, cle, Environment.UserName, options.Groupes));
    }

    /// <summary>
    /// Groupes AD de l'utilisateur en noms « DOMAINE\Groupe » : le principal Windows ne porte que des SID,
    /// sur lesquels IsInRole(nom) ne s'applique pas.
    /// </summary>
    private static HashSet<string> GroupesWindows(ClaimsPrincipal principal)
    {
        var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (principal.Identity is WindowsIdentity { Groups: { } groupes })
            foreach (var sid in groupes)
                try { noms.Add(sid.Translate(typeof(NTAccount)).Value); }
                catch (IdentityNotMappedException) { }
        return noms;
    }

    private static JetonEmis Emettre(OptionsJeton options, SymmetricSecurityKey cle, string utilisateur, IReadOnlyList<string> roles)
    {
        var expiration = DateTime.UtcNow.AddMinutes(options.DureeMinutes);
        var claims = new Dictionary<string, object>
        {
            [ClaimNom] = utilisateur,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
        };
        if (roles.Count > 0) claims[ClaimRole] = roles.ToArray();

        var jeton = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Emetteur,
            Audience = options.Audience,
            Claims = claims,
            Expires = expiration,
            SigningCredentials = new SigningCredentials(cle, SecurityAlgorithms.HmacSha256)
        });
        return new JetonEmis(jeton, expiration, utilisateur, roles);
    }

    private static SymmetricSecurityKey CleSignature(OptionsJeton options)
    {
        if (options.Cle is not { Length: >= 32 } cle)
            throw new InvalidOperationException(
                "Oim:Securite:Jeton:Cle doit contenir au moins 32 caractères (clé de signature des jetons, identique sur tous les serveurs).");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cle));
    }
}
