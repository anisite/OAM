using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OAM.Api.Auth;

/// <summary>
/// Endpoint de création du JWT via authentification NTLM.
/// C'est le seul endpoint qui utilise NTLM — tous les autres utilisent JWT Bearer.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(IOptions<JwtSettings> jwtSettings) : ControllerBase
{
    [HttpGet("token")]
    [Authorize(AuthenticationSchemes = NegotiateDefaults.AuthenticationScheme)]
    public IActionResult ObtenirToken()
    {
        var settings = jwtSettings.Value;
        var identity = HttpContext.User.Identity;

        if (identity?.IsAuthenticated != true)
            return Unauthorized();

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, identity.Name ?? "inconnu"),
            new(ClaimTypes.AuthenticationMethod, "NTLM"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // Ajouter les groupes/rôles AD comme claims
        if (HttpContext.User.Identity is System.Security.Principal.WindowsIdentity windowsIdentity)
        {
            var groups = windowsIdentity.Groups?
                .Select(g => g.Translate(typeof(System.Security.Principal.NTAccount)))
                .Select(g => g.ToString());

            if (groups is not null)
            {
                foreach (var group in groups)
                    claims.Add(new Claim(ClaimTypes.Role, group));
            }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(settings.ExpirationMinutes),
            signingCredentials: creds);

        return Ok(new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            expiration = token.ValidTo,
            utilisateur = identity.Name
        });
    }
}
