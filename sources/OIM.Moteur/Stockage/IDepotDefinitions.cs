using OIM.Moteur.Definitions;

namespace OIM.Moteur.Stockage;

public sealed record VersionDefinition(
    string DefinitionId,
    int Version,
    string Yaml,
    IReadOnlyDictionary<string, string> Fichiers,
    string Empreinte,
    string? DeployePar,
    DateTime DeployeLe,
    string? Commentaire)
{
    public PaquetDefinition Paquet() => new(Yaml, Fichiers.ToDictionary());
}

public sealed record ResumeDefinition(
    string Id,
    string Equipe,
    string? Nom,
    string? Description,
    int VersionCourante,
    bool Actif,
    DateTime ModifieLe,
    string? DeployePar,
    int NbVersions);

public sealed record ResumeVersion(int Version, string Empreinte, string? DeployePar, DateTime DeployeLe, string? Commentaire);

public sealed record ResultatDeploiement(string Id, int Version, bool NouvelleVersion);

/// <summary>
/// Dépôt des définitions de processus. Chaque déploiement dont le contenu diffère crée
/// une nouvelle version immuable; les instances en cours restent liées à leur version.
/// </summary>
public interface IDepotDefinitions
{
    Task InitialiserAsync(CancellationToken ct = default);

    /// <summary>Déploie dans l'équipe : l'id enregistré est qualifié (« equipe.processus »).</summary>
    Task<ResultatDeploiement> DeployerAsync(string equipe, DefinitionProcessus definition, PaquetDefinition paquet,
        string? deployePar, string? commentaire, CancellationToken ct = default);

    /// <summary>Version précise, ou version courante si <paramref name="version"/> est null.</summary>
    Task<VersionDefinition?> ObtenirAsync(string id, int? version = null, CancellationToken ct = default);

    /// <param name="equipes">Équipes à inclure, ou null pour toutes.</param>
    Task<IReadOnlyList<ResumeDefinition>> ListerAsync(IReadOnlyCollection<string>? equipes, CancellationToken ct = default);

    Task<IReadOnlyList<ResumeVersion>> VersionsAsync(string id, CancellationToken ct = default);

    Task<bool> DefinirActifAsync(string id, bool actif, CancellationToken ct = default);

    /// <summary>
    /// Brouillon : paquet non déployé, exécuté par les tests métier. Son id commence par
    /// <see cref="PrefixeBrouillon"/>; <see cref="ObtenirAsync"/> le retourne comme une version 1.
    /// </summary>
    Task EnregistrerBrouillonAsync(string id, PaquetDefinition paquet, CancellationToken ct = default);

    Task SupprimerBrouillonAsync(string id, CancellationToken ct = default);

    public const string PrefixeBrouillon = "~";
}
