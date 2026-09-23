using System.Security.Claims;

namespace OIM.Api;

public static class Securite
{
    public const string Politique = "Oim";

    /// <summary>Utilisateur courant (authentification Windows), ou compte local en développement.</summary>
    public static string Utilisateur(this ClaimsPrincipal? principal) =>
        principal?.Identity is { IsAuthenticated: true, Name: { } nom } ? nom : Environment.UserName;
}
