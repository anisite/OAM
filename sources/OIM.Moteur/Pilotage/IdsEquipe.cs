using System.Text.RegularExpressions;
using OIM.Moteur.Stockage;

namespace OIM.Moteur.Pilotage;

/// <summary>
/// Identifiants qualifiés par l'équipe : processus <c>equipe.processus</c> (le YAML garde son id court)
/// et instances <c>equipe.id</c>. L'id d'équipe ne contient jamais de point : on découpe au premier.
/// Le préfixe des instances permet de filtrer une équipe sur l'index (TaskHub, InstanceID) et évite
/// les collisions d'instanceId entre équipes. Les sous-processus héritent du préfixe de leur parent.
/// </summary>
public static partial class IdsEquipe
{
    public const char Separateur = '.';

    /// <summary>Segments d'URL de l'interface qui ne peuvent pas être des ids d'équipe.</summary>
    private static readonly HashSet<string> Reserves = ["admin", "accessibilite", "api", "auth", "assets"];

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,48}[a-z0-9])?$")]
    private static partial Regex FormatEquipe();

    public static bool EstIdEquipeValide(string? id) => id is not null && FormatEquipe().IsMatch(id) && !Reserves.Contains(id);

    public static string Qualifier(string equipe, string id) => $"{equipe}{Separateur}{id}";

    /// <summary>
    /// Équipe d'un id qualifié (processus, instance, brouillon « ~equipe.x~… »), ou null s'il n'est pas qualifié.
    /// </summary>
    public static string? Equipe(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id.StartsWith(IDepotDefinitions.PrefixeBrouillon, StringComparison.Ordinal)) id = id[1..];
        var i = id.IndexOf(Separateur);
        return i > 0 ? id[..i] : null;
    }

    /// <summary>Partie propre à l'équipe (id du YAML pour un processus).</summary>
    public static string Local(string id)
    {
        var i = id.IndexOf(Separateur);
        return i >= 0 ? id[(i + 1)..] : id;
    }
}
