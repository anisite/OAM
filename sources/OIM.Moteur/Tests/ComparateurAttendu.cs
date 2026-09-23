using System.Text.Json.Nodes;
using OIM.Moteur.Expressions;

namespace OIM.Moteur.Tests;

/// <summary>
/// Comparaison partielle « attendu ⊆ obtenu » : seules les clés présentes dans l'attendu sont
/// vérifiées; les listes doivent avoir la même longueur et sont comparées élément par élément.
/// Les valeurs scalaires suivent l'égalité des expressions (12 == "12", true == "true").
/// </summary>
public static class ComparateurAttendu
{
    private static readonly Dictionary<string, string> AliasStatuts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["termine"] = "Completed", ["terminee"] = "Completed", ["terminé"] = "Completed", ["terminée"] = "Completed",
        ["encours"] = "Running", ["enattente"] = "Running",
        ["echec"] = "Failed", ["enechec"] = "Failed", ["échec"] = "Failed",
        ["interrompue"] = "Terminated", ["suspendue"] = "Suspended"
    };

    public static List<string> Comparer(JsonObject attendu, JsonObject obtenu)
    {
        var ecarts = new List<string>();
        foreach (var (cle, valeur) in attendu)
        {
            var reel = obtenu[cle];
            switch (cle)
            {
                case "statut":
                    var voulu = Expression.EnTexte(valeur);
                    voulu = AliasStatuts.GetValueOrDefault(voulu.Replace(" ", ""), voulu);
                    if (!string.Equals(voulu, Expression.EnTexte(reel), StringComparison.OrdinalIgnoreCase))
                        ecarts.Add($"statut : attendu {voulu}, obtenu {Expression.EnTexte(reel)}"
                                   + (obtenu["erreur"] is { } e ? $" ({Expression.EnTexte(e)})" : ""));
                    break;

                case "erreur":
                    // Contient : le message complet inclut souvent l'URL ou le détail technique.
                    var extrait = Expression.EnTexte(valeur);
                    if (!Expression.EnTexte(reel).Contains(extrait, StringComparison.OrdinalIgnoreCase))
                        ecarts.Add($"erreur : « {extrait} » attendu dans « {Expression.EnTexte(reel)} »");
                    break;

                default:
                    Comparer(valeur, reel, cle, ecarts);
                    break;
            }
        }
        return ecarts;
    }

    private static void Comparer(JsonNode? attendu, JsonNode? obtenu, string chemin, List<string> ecarts)
    {
        switch (attendu)
        {
            case JsonObject o:
                if (obtenu is not JsonObject r)
                {
                    ecarts.Add($"{chemin} : objet attendu, obtenu {Apercu(obtenu)}");
                    return;
                }
                foreach (var (cle, valeur) in o) Comparer(valeur, r[cle], $"{chemin}.{cle}", ecarts);
                break;

            case JsonArray a:
                if (obtenu is not JsonArray l)
                {
                    ecarts.Add($"{chemin} : liste attendue, obtenu {Apercu(obtenu)}");
                    return;
                }
                if (a.Count != l.Count)
                    ecarts.Add($"{chemin} : {a.Count} élément(s) attendu(s), {l.Count} obtenu(s) — obtenu {Apercu(obtenu)}");
                for (var i = 0; i < Math.Min(a.Count, l.Count); i++) Comparer(a[i], l[i], $"{chemin}[{i}]", ecarts);
                break;

            default:
                if (!Expression.Egal(attendu, obtenu))
                    ecarts.Add($"{chemin} : attendu {Apercu(attendu)}, obtenu {Apercu(obtenu)}");
                break;
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions Lisible =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Apercu(JsonNode? n) => n is null ? "(rien)" : Gabarit.Tronquer(n.ToJsonString(Lisible), 200);
}
