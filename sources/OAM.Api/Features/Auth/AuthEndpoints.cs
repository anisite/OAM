using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OAM.Api.Auth;

namespace OAM.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/dev-token", (IOptions<JwtSettings> opts, IWebHostEnvironment env) =>
        {
            if (!env.IsDevelopment()) return Results.NotFound();

            var token = GenererToken(opts.Value, Environment.UserName, "Dev");
            return Results.Ok(new { token = token.valeur, expiration = token.expiration, utilisateur = Environment.UserName });
        }).AllowAnonymous();

        app.MapGet("/api/auth/token", (IOptions<JwtSettings> opts, HttpContext ctx) =>
        {
            var identity = ctx.User.Identity;
            if (identity?.IsAuthenticated != true) return Results.Unauthorized();

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, identity.Name ?? "inconnu"),
                new(ClaimTypes.AuthenticationMethod, "NTLM"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            if (identity is System.Security.Principal.WindowsIdentity wi)
            {
                var groupes = wi.Groups?
                    .Select(g => g.Translate(typeof(System.Security.Principal.NTAccount)).ToString());
                if (groupes is not null)
                    foreach (var g in groupes)
                        claims.Add(new Claim(ClaimTypes.Role, g));
            }

            var token = GenererToken(opts.Value, identity.Name ?? "inconnu", "NTLM", claims);
            return Results.Ok(new { token = token.valeur, expiration = token.expiration, utilisateur = identity.Name });
        }).RequireAuthorization(p => p.AddAuthenticationSchemes(NegotiateDefaults.AuthenticationScheme).RequireAuthenticatedUser());

        return app;
    }

    private static (string valeur, DateTime expiration) GenererToken(JwtSettings settings, string nom, string methode, List<Claim>? claimsExtra = null)
    {
        var claims = claimsExtra ?? [
            new(ClaimTypes.Name, nom),
            new(ClaimTypes.AuthenticationMethod, methode),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret));
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(settings.ExpirationMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), token.ValidTo);
    }
}
