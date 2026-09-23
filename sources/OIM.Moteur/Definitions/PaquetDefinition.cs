using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace OIM.Moteur.Definitions;

/// <summary>
/// Unité de déploiement : le YAML du processus et ses fichiers annexes
/// (gabarits de requêtes YamlHttpClient, gabarits de courriels…), référencés
/// par chemin relatif depuis le YAML.
/// </summary>
public sealed class PaquetDefinition
{
    public string Yaml { get; }
    public IReadOnlyDictionary<string, string> Fichiers { get; }

    public PaquetDefinition(string yaml, IDictionary<string, string>? fichiers = null)
    {
        Yaml = yaml;
        Fichiers = (fichiers ?? new Dictionary<string, string>())
            .ToDictionary(p => NormaliserChemin(p.Key), p => p.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static string NormaliserChemin(string chemin) =>
        chemin.Replace('\\', '/').TrimStart('.', '/').Trim();

    /// <summary>Empreinte SHA-256 du contenu (YAML + fichiers triés) : détecte un redéploiement identique.</summary>
    public string Empreinte()
    {
        var sb = new StringBuilder(Yaml.ReplaceLineEndings("\n"));
        foreach (var (chemin, contenu) in Fichiers.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            sb.Append('\0').Append(chemin.ToLowerInvariant()).Append('\0').Append(contenu.ReplaceLineEndings("\n"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public string? TrouverFichier(string chemin) =>
        Fichiers.TryGetValue(NormaliserChemin(chemin), out var c) ? c : null;

    /// <summary>Gabarit de requête HTTP : <c>fichier.yml</c> ou <c>fichier.yml#cle</c>, cherché aussi sous <c>requetes/</c>.</summary>
    public (string Chemin, string Contenu)? TrouverRequete(string reference)
    {
        var chemin = reference.Split('#')[0];
        foreach (var candidat in new[] { chemin, $"requetes/{chemin}", $"{chemin}.yml", $"requetes/{chemin}.yml" })
            if (TrouverFichier(candidat) is { } c) return (NormaliserChemin(candidat), c);
        return null;
    }

    /// <summary>Gabarit de courriel : <c>nom</c> → <c>gabarits/nom.yml</c>, <c>nom.yml</c> ou chemin exact.</summary>
    public (string Chemin, string Contenu)? TrouverGabarit(string nom)
    {
        foreach (var candidat in new[] { $"gabarits/{nom}.yml", $"gabarits/{nom}.yaml", $"{nom}.yml", $"{nom}.yaml", nom, $"gabarits/{nom}" })
            if (TrouverFichier(candidat) is { } c) return (NormaliserChemin(candidat), c);
        return null;
    }

    /// <summary>
    /// Lit une archive zip. Le YAML du processus est le fichier .yml/.yaml
    /// contenant une clé racine <c>etapes</c>; tous les autres fichiers texte deviennent des annexes.
    /// </summary>
    public static PaquetDefinition DepuisZip(Stream flux)
    {
        using var zip = new ZipArchive(flux, ZipArchiveMode.Read);
        var fichiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entree in zip.Entries.Where(e => e.Length > 0 && !e.FullName.EndsWith('/')))
        {
            using var lecteur = new StreamReader(entree.Open(), Encoding.UTF8);
            fichiers[NormaliserChemin(entree.FullName)] = lecteur.ReadToEnd();
        }

        // Si l'archive contient un seul dossier racine, on le retire des chemins.
        var racines = fichiers.Keys.Select(k => k.Contains('/') ? k[..k.IndexOf('/')] : string.Empty).Distinct().ToList();
        if (racines.Count == 1 && racines[0].Length > 0)
            fichiers = fichiers.ToDictionary(p => p.Key[(racines[0].Length + 1)..], p => p.Value, StringComparer.OrdinalIgnoreCase);

        return DepuisFichiers(fichiers);
    }

    /// <summary>Construit un paquet à partir d'un dossier (déploiement par dépôt de fichiers).</summary>
    public static PaquetDefinition DepuisDossier(string dossier)
    {
        var fichiers = Directory.EnumerateFiles(dossier, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith('.'))
            .ToDictionary(f => NormaliserChemin(Path.GetRelativePath(dossier, f)), File.ReadAllText, StringComparer.OrdinalIgnoreCase);
        return DepuisFichiers(fichiers);
    }

    public static PaquetDefinition DepuisFichiers(Dictionary<string, string> fichiers)
    {
        var principal = TrouverYamlPrincipal(fichiers)
                        ?? throw new InvalidDataException("Aucun fichier YAML de processus (avec une clé racine « etapes ») n'a été trouvé dans le paquet.");
        var yaml = fichiers[principal];
        fichiers.Remove(principal);
        return new PaquetDefinition(yaml, fichiers);
    }

    public static string? TrouverYamlPrincipal(IReadOnlyDictionary<string, string> fichiers)
    {
        var candidats = fichiers
            .Where(p => p.Key.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || p.Key.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .Where(p => System.Text.RegularExpressions.Regex.IsMatch(p.Value, @"^etapes\s*:", System.Text.RegularExpressions.RegexOptions.Multiline))
            .Select(p => p.Key)
            .OrderBy(k => k.Count(c => c == '/'))
            .ToList();
        return candidats.FirstOrDefault(k => k.Equals("processus.yml", StringComparison.OrdinalIgnoreCase)) ?? candidats.FirstOrDefault();
    }

    public byte[] VersZip(string nomPrincipal)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Ecrire(zip, nomPrincipal, Yaml);
            foreach (var (chemin, contenu) in Fichiers) Ecrire(zip, chemin, contenu);
        }
        return ms.ToArray();

        static void Ecrire(ZipArchive zip, string chemin, string contenu)
        {
            using var w = new StreamWriter(zip.CreateEntry(chemin).Open(), new UTF8Encoding(false));
            w.Write(contenu);
        }
    }
}
