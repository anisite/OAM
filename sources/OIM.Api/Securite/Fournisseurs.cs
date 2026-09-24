using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace OIM.Api.Securite;

/// <summary>
/// Authentification par jeton Bearer, quel que soit l'émetteur : un schéma JwtBearer par fournisseur,
/// et un schéma « Bearer » qui choisit le fournisseur d'après le claim « iss » du jeton. Une fois le
/// jeton validé, ses sujets (claims <see cref="TypeSujet"/>) sont la seule chose qu'OIM regarde.
/// </summary>
public static class Fournisseurs
{
    public const string SchemaBearer = "Bearer";
    public const string Politique = "Oim";
    public const string PolitiqueWindows = "OimWindows";

    /// <summary>Claim ajouté à l'identité validée : un sujet (nom ou groupe, préfixé selon le fournisseur).</summary>
    public const string TypeSujet = "oim:sujet";

    public static void AjouterSecurite(this IServiceCollection services, OptionsSecurite options)
    {
        Valider(options);
        var auth = services.AddAuthentication(SchemaBearer);
        var parEmetteur = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (nom, f) in options.Fournisseurs)
        {
            var schema = $"{SchemaBearer}:{nom}";
            parEmetteur[f.Emetteur!] = schema;
            auth.AddJwtBearer(schema, o =>
            {
                o.MapInboundClaims = false;
                if (f.Autorite is not null)
                {
                    o.Authority = f.Autorite;
                    o.Audience = f.Audience;
                }
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = f.Emetteur,
                    ValidAudience = f.Audience,
                    ValidateAudience = f.Audience is not null,
                    IssuerSigningKey = f.Cle is null ? null : CleSignature(f.Cle),
                    NameClaimType = f.ClaimNom,
                    RoleClaimType = f.ClaimGroupes,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                o.Events = new JwtBearerEvents { OnTokenValidated = c => AjouterSujets(c, f) };
            });
        }

        var parDefaut = parEmetteur.Values.First();
        auth.AddPolicyScheme(SchemaBearer, "Jeton Bearer", o => o.ForwardDefaultSelector = http =>
            Emetteur(http.Request.Headers.Authorization.ToString()) is { } iss && parEmetteur.TryGetValue(iss, out var schema)
                ? schema
                : parDefaut);

        if (options.Fournisseurs.Values.Any(f => f.Type == TypeFournisseur.Interne))
            auth.AddNegotiate();

        services.AddAuthorization(o =>
        {
            o.AddPolicy(Politique, p => p.AddAuthenticationSchemes(SchemaBearer).RequireAuthenticatedUser());
            o.AddPolicy(PolitiqueWindows, p => p.AddAuthenticationSchemes(NegotiateDefaults.AuthenticationScheme).RequireAuthenticatedUser());
        });
    }

    /// <summary>Sujets du jeton : le nom et chaque groupe, avec le préfixe du fournisseur.</summary>
    private static Task AjouterSujets(TokenValidatedContext c, OptionsFournisseur f)
    {
        if (c.Principal?.Identity is not ClaimsIdentity identite) return Task.CompletedTask;
        var sujets = identite.FindAll(f.ClaimNom).Concat(identite.FindAll(f.ClaimGroupes))
            .Select(claim => f.Prefixe + claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var sujet in sujets) identite.AddClaim(new Claim(TypeSujet, sujet));
        return Task.CompletedTask;
    }

    /// <summary>Émetteur (« iss ») d'un jeton non encore validé, pour choisir le fournisseur qui le validera.</summary>
    private static string? Emetteur(string entete)
    {
        if (!entete.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var jeton = entete[7..].Trim();
        var lecteur = new JsonWebTokenHandler();
        try
        {
            return lecteur.CanReadToken(jeton) ? lecteur.ReadJsonWebToken(jeton).Issuer : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static SymmetricSecurityKey CleSignature(string cle) => new(Encoding.UTF8.GetBytes(cle));

    private static void Valider(OptionsSecurite options)
    {
        if (options.Fournisseurs.Count == 0)
            throw new InvalidOperationException("Oim:Securite:Fournisseurs : au moins un fournisseur de jetons est requis.");
        if (options.Fournisseurs.Values.Count(f => f.Type == TypeFournisseur.Interne) > 1)
            throw new InvalidOperationException("Oim:Securite:Fournisseurs : un seul fournisseur Interne est permis.");

        foreach (var (nom, f) in options.Fournisseurs)
        {
            var section = $"Oim:Securite:Fournisseurs:{nom}";
            if (string.IsNullOrWhiteSpace(f.Emetteur))
                throw new InvalidOperationException($"{section}:Emetteur est requis.");
            if (f.Cle is { Length: < 32 } || (f.Type == TypeFournisseur.Interne && f.Cle is null))
                throw new InvalidOperationException(
                    $"{section}:Cle doit contenir au moins 32 caractères (clé de signature HS256, identique sur tous les serveurs).");
            if (f.Type == TypeFournisseur.Externe && f.Cle is null && f.Autorite is null)
                throw new InvalidOperationException($"{section} : Autorite (OIDC) ou Cle est requise.");
        }

        if (options.Fournisseurs.Values.GroupBy(f => f.Emetteur).FirstOrDefault(g => g.Count() > 1) is { } doublon)
            throw new InvalidOperationException($"Oim:Securite:Fournisseurs : émetteur « {doublon.Key} » en double.");
    }
}
