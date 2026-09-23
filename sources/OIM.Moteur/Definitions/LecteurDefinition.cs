using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;

namespace OIM.Moteur.Definitions;

public sealed record Diagnostic(string Gravite, string Message, string? Etape = null, int? Ligne = null)
{
    public const string Erreur = "erreur";
    public const string Avertissement = "avertissement";
}

public sealed class ResultatLecture
{
    public DefinitionProcessus? Definition { get; init; }
    public JsonNode? Brut { get; init; }
    public List<Diagnostic> Diagnostics { get; } = [];
    public bool Valide => Definition is not null && Diagnostics.All(d => d.Gravite != Diagnostic.Erreur);
    public IEnumerable<Diagnostic> Erreurs => Diagnostics.Where(d => d.Gravite == Diagnostic.Erreur);
}

/// <summary>
/// Lit le YAML d'un processus et produit le modèle <see cref="DefinitionProcessus"/>.
/// La lecture est tolérante : elle accumule les problèmes dans <see cref="ResultatLecture.Diagnostics"/>
/// afin que le concepteur et l'API de validation puissent tous les afficher d'un coup.
/// </summary>
public static class LecteurDefinition
{
    private static readonly HashSet<string> ClesCommunes =
        ["id", "type", "description", "statut", "message", "suivant", "fin", "siErreur", "retry"];

    public static ResultatLecture Lire(string yaml)
    {
        JsonNode? brut;
        try
        {
            brut = YamlJson.Lire(yaml);
        }
        catch (YamlException ex)
        {
            var r = new ResultatLecture();
            r.Diagnostics.Add(new Diagnostic(Diagnostic.Erreur,
                $"YAML invalide : {ex.InnerException?.Message ?? ex.Message}", null, (int)ex.Start.Line));
            return r;
        }

        if (brut is not JsonObject racine)
        {
            var r = new ResultatLecture { Brut = brut };
            r.Diagnostics.Add(new Diagnostic(Diagnostic.Erreur, "Le document doit être un objet YAML (id, nom, entrees, etapes)."));
            return r;
        }

        var diags = new List<Diagnostic>();
        var id = Texte(racine, "id");
        if (string.IsNullOrWhiteSpace(id))
            diags.Add(new Diagnostic(Diagnostic.Erreur, "L'identifiant du processus (id) est obligatoire."));
        else if (!EstIdentifiantValide(id))
            diags.Add(new Diagnostic(Diagnostic.Erreur,
                $"L'identifiant « {id} » ne doit contenir que des lettres, chiffres, tirets, points ou soulignés."));

        var transitionsMax = 1000;
        if (racine["limites"] is JsonObject limites && limites["transitions"] is JsonValue tv)
        {
            if (tv.TryGetValue<int>(out var t) && t > 0) transitionsMax = t;
            else diags.Add(new Diagnostic(Diagnostic.Erreur, "limites.transitions doit être un entier positif."));
        }

        var definition = new DefinitionProcessus
        {
            Id = id ?? string.Empty,
            Nom = Texte(racine, "nom"),
            Description = Texte(racine, "description"),
            Entrees = LireEntrees(racine["entrees"], diags),
            Etapes = LireEtapes(racine["etapes"], diags),
            TransitionsMax = transitionsMax
        };

        var resultat = new ResultatLecture { Definition = definition, Brut = racine };
        resultat.Diagnostics.AddRange(diags);
        return resultat;
    }

    public static bool EstIdentifiantValide(string id) =>
        id.Length is > 0 and <= 100 && id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');

    private static List<ParametreEntree> LireEntrees(JsonNode? noeud, List<Diagnostic> diags)
    {
        var liste = new List<ParametreEntree>();
        if (noeud is null) return liste;
        if (noeud is not JsonObject obj)
        {
            diags.Add(new Diagnostic(Diagnostic.Erreur, "« entrees » doit être un dictionnaire nom → { type, requis }."));
            return liste;
        }

        foreach (var (nom, def) in obj)
        {
            // Forme abrégée : « courriel: string »
            if (def is JsonValue v && v.TryGetValue<string>(out var typeAbrege))
            {
                liste.Add(new ParametreEntree { Nom = nom, Type = typeAbrege });
                continue;
            }

            var o = def as JsonObject ?? [];
            liste.Add(new ParametreEntree
            {
                Nom = nom,
                Type = Texte(o, "type") ?? "string",
                Requis = Booleen(o, "requis"),
                Defaut = o["defaut"]?.DeepClone(),
                Libelle = Texte(o, "libelle"),
                Description = Texte(o, "description")
            });
        }
        return liste;
    }

    private static List<Etape> LireEtapes(JsonNode? noeud, List<Diagnostic> diags)
    {
        var etapes = new List<Etape>();
        if (noeud is not JsonArray tableau)
        {
            diags.Add(new Diagnostic(Diagnostic.Erreur, "« etapes » est obligatoire et doit être une liste."));
            return etapes;
        }

        var index = 0;
        foreach (var element in tableau)
        {
            index++;
            if (element is not JsonObject e)
            {
                diags.Add(new Diagnostic(Diagnostic.Erreur, $"L'étape n° {index} doit être un objet."));
                continue;
            }

            var id = Texte(e, "id") ?? $"etape{index}";
            if (Texte(e, "id") is null)
                diags.Add(new Diagnostic(Diagnostic.Erreur, $"L'étape n° {index} n'a pas d'identifiant (id).", id));

            var proprietes = new JsonObject();
            foreach (var (cle, valeur) in e)
                if (!ClesCommunes.Contains(cle))
                    proprietes[cle] = valeur?.DeepClone();

            etapes.Add(new Etape
            {
                Id = id,
                Type = Texte(e, "type") ?? string.Empty,
                Description = Texte(e, "description"),
                Statut = Texte(e, "statut"),
                Message = Texte(e, "message"),
                Fin = Booleen(e, "fin"),
                Suivant = LireSuivant(e["suivant"], id, diags),
                SiErreur = Texte(e, "siErreur"),
                Retry = LireRetry(e["retry"], id, diags),
                Proprietes = proprietes
            });
        }
        return etapes;
    }

    /// <summary>
    /// <c>suivant</c> accepte trois formes :
    /// <c>suivant: etape</c>, ou une liste de <c>{ si: expr, aller: etape }</c> terminée
    /// optionnellement par <c>{ sinon: etape }</c>.
    /// </summary>
    private static List<Branche> LireSuivant(JsonNode? noeud, string etape, List<Diagnostic> diags)
    {
        var branches = new List<Branche>();
        switch (noeud)
        {
            case null:
                break;
            case JsonValue v:
                branches.Add(new Branche(null, v.ToString()));
                break;
            case JsonArray liste:
                foreach (var item in liste)
                {
                    if (item is JsonValue simple)
                    {
                        branches.Add(new Branche(null, simple.ToString()));
                        continue;
                    }
                    if (item is not JsonObject b)
                    {
                        diags.Add(new Diagnostic(Diagnostic.Erreur, "Branche « suivant » invalide.", etape));
                        continue;
                    }
                    if (Texte(b, "sinon") is { } sinon)
                        branches.Add(new Branche(null, sinon));
                    else if (Texte(b, "aller") is { } aller)
                    {
                        var si = Texte(b, "si");
                        if (si is null)
                            diags.Add(new Diagnostic(Diagnostic.Erreur, $"La branche vers « {aller} » n'a pas de condition « si ».", etape));
                        branches.Add(new Branche(si ?? "false", aller));
                    }
                    else
                        diags.Add(new Diagnostic(Diagnostic.Erreur, "Chaque branche doit avoir « si » + « aller », ou « sinon ».", etape));
                }
                break;
            default:
                diags.Add(new Diagnostic(Diagnostic.Erreur, "« suivant » doit être un identifiant d'étape ou une liste de branches.", etape));
                break;
        }
        return branches;
    }

    private static PolitiqueReprise? LireRetry(JsonNode? noeud, string etape, List<Diagnostic> diags)
    {
        if (noeud is null) return null;
        if (noeud is not JsonObject r)
        {
            diags.Add(new Diagnostic(Diagnostic.Erreur, "« retry » doit être un objet { tentatives, delai, backoff }.", etape));
            return null;
        }

        var tentatives = r["tentatives"] is JsonValue tv && tv.TryGetValue<int>(out var t) ? t : 3;
        if (tentatives < 1)
            diags.Add(new Diagnostic(Diagnostic.Erreur, "retry.tentatives doit être ≥ 1.", etape));

        var delai = TimeSpan.FromSeconds(5);
        if (Texte(r, "delai") is { } d && !Duree.TryLire(d, out delai))
            diags.Add(new Diagnostic(Diagnostic.Erreur, $"retry.delai « {d} » n'est pas une durée valide.", etape));

        var backoff = 1d;
        if (r["backoff"] is JsonValue bv && !(bv.TryGetValue<double>(out backoff) || TryInt(bv, out backoff)))
            diags.Add(new Diagnostic(Diagnostic.Erreur, "retry.backoff doit être un nombre.", etape));

        TimeSpan? delaiMax = null;
        if (Texte(r, "delaiMax") is { } dm)
        {
            if (Duree.TryLire(dm, out var m)) delaiMax = m;
            else diags.Add(new Diagnostic(Diagnostic.Erreur, $"retry.delaiMax « {dm} » n'est pas une durée valide.", etape));
        }

        return new PolitiqueReprise(Math.Max(1, tentatives), delai, backoff <= 0 ? 1 : backoff, delaiMax);
    }

    private static bool TryInt(JsonValue v, out double d)
    {
        var ok = v.TryGetValue<int>(out var i);
        d = i;
        return ok;
    }

    internal static string? Texte(JsonObject o, string cle) => o[cle] switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        JsonValue v => v.ToJsonString().Trim('"'),
        var n => n.ToJsonString()
    };

    private static bool Booleen(JsonObject o, string cle) =>
        o[cle] is JsonValue v && (v.TryGetValue<bool>(out var b) ? b : v.TryGetValue<string>(out var s) && s is "true" or "oui");
}
