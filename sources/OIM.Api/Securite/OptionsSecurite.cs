namespace OIM.Api.Securite;

/// <summary>Section « Oim:Securite ».</summary>
public sealed class OptionsSecurite
{
    public const string Section = "Oim:Securite";

    /// <summary>false (développement) : aucun jeton exigé, l'appelant est administrateur.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Sujets administrateurs OIM : toutes les équipes, déploiement, gestion des équipes.</summary>
    public string[] Administrateurs { get; set; } = [];

    /// <summary>Sujets du support OIM : voient et pilotent toutes les équipes, sans déployer.</summary>
    public string[] Support { get; set; } = [];

    /// <summary>Émetteurs de jetons acceptés, par nom (ex. « oim », « entra »).</summary>
    public Dictionary<string, OptionsFournisseur> Fournisseurs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum TypeFournisseur
{
    /// <summary>Jeton émis par OIM (/api/auth/jeton, authentification Windows), signé HS256.</summary>
    Interne,

    /// <summary>Jeton d'un autre émetteur, seulement validé (métadonnées OIDC de l'autorité, ou clé HS256).</summary>
    Externe
}

/// <summary>
/// Un émetteur de jetons. Quel que soit l'émetteur, un jeton valide se résume à un nom et à des
/// <em>sujets</em> : le nom et les valeurs du claim de groupes, précédés de <see cref="Prefixe"/>.
/// Les membres d'équipe, administrateurs et support sont des sujets.
/// </summary>
public sealed class OptionsFournisseur
{
    public TypeFournisseur Type { get; set; } = TypeFournisseur.Interne;

    /// <summary>Claim « iss » attendu : sert aussi à reconnaître le fournisseur d'un jeton.</summary>
    public string? Emetteur { get; set; }

    public string? Audience { get; set; }

    /// <summary>Clé HS256 (au moins 32 caractères), identique sur tous les serveurs.</summary>
    public string? Cle { get; set; }

    /// <summary>Externe : autorité OIDC (clés de signature lues dans ses métadonnées).</summary>
    public string? Autorite { get; set; }

    public string ClaimNom { get; set; } = "name";
    public string ClaimGroupes { get; set; } = "role";

    /// <summary>Préfixe des sujets (ex. « entra: ») : évite qu'un groupe d'un émetteur soit confondu avec celui d'un autre.</summary>
    public string Prefixe { get; set; } = "";

    /// <summary>Interne : durée de vie des jetons émis.</summary>
    public int DureeMinutes { get; set; } = 480;
}
