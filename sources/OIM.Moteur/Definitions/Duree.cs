using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace OIM.Moteur.Definitions;

/// <summary>
/// Lecture des durées exprimées dans les définitions. Formats acceptés :
/// <list type="bullet">
///   <item>TimeSpan .NET : <c>00:00:30</c>, <c>5.00:00:00</c></item>
///   <item>Abrégé : <c>30s</c>, <c>15m</c>, <c>2h</c>, <c>5j</c> (ou <c>5d</c>)</item>
///   <item>ISO 8601 : <c>PT30S</c>, <c>P5D</c></item>
/// </list>
/// </summary>
public static partial class Duree
{
    [GeneratedRegex(@"^(?<n>\d+(?:[.,]\d+)?)\s*(?<u>ms|s|m|h|j|d)$", RegexOptions.IgnoreCase)]
    private static partial Regex Abrege();

    public static bool TryLire(string? texte, out TimeSpan duree)
    {
        duree = default;
        if (string.IsNullOrWhiteSpace(texte)) return false;
        texte = texte.Trim();

        var m = Abrege().Match(texte);
        if (m.Success)
        {
            var n = double.Parse(m.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            duree = m.Groups["u"].Value.ToLowerInvariant() switch
            {
                "ms" => TimeSpan.FromMilliseconds(n),
                "s" => TimeSpan.FromSeconds(n),
                "m" => TimeSpan.FromMinutes(n),
                "h" => TimeSpan.FromHours(n),
                _ => TimeSpan.FromDays(n)
            };
            return true;
        }

        if (texte.StartsWith('P') || texte.StartsWith('p'))
        {
            try
            {
                duree = XmlConvert.ToTimeSpan(texte.ToUpperInvariant());
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return TimeSpan.TryParse(texte, CultureInfo.InvariantCulture, out duree);
    }

    public static TimeSpan Lire(string? texte, string contexte) =>
        TryLire(texte, out var d)
            ? d
            : throw new FormatException($"{contexte} : durée « {texte} » invalide (ex. 00:00:30, 5.00:00:00, 30s, 5j, P5D).");
}
