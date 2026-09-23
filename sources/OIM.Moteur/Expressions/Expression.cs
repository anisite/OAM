using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OIM.Moteur.Expressions;

public sealed class ErreurExpression(string message) : Exception(message);

/// <summary>
/// Petit langage d'expressions utilisé entre <c>{{ }}</c> dans les définitions.
/// <code>
///   entrees.dossierId                       chemins (points et [index])
///   evenement.approuve == true              == != &gt; &lt; &gt;= &lt;=
///   a &amp;&amp; (b || !c)   /  a et (b ou non c)    logique (aussi and/or/not)
///   montant * 1.15 + 2                      arithmétique, + concatène les textes
///   'texte' "texte" 12 3.5 true false null  littéraux
///   longueur(x) vide(x) contient(a, b)      fonctions (voir <see cref="Fonctions"/>)
/// </code>
/// L'évaluation est pure et déterministe : aucune fonction ne lit l'horloge ou l'environnement,
/// ce qui permet de l'utiliser sans risque dans une orchestration DurableTask.
/// </summary>
public sealed class Expression
{
    private static readonly ConcurrentDictionary<string, Expression> Cache = new();

    private readonly Noeud _racine;
    public string Source { get; }

    private Expression(string source, Noeud racine)
    {
        Source = source;
        _racine = racine;
    }

    public static Expression Analyser(string source) =>
        Cache.GetOrAdd(source, s => new Expression(s, new Analyseur(s).AnalyserTout()));

    public JsonNode? Evaluer(JsonObject contexte) => _racine.Evaluer(contexte);

    /// <summary>Chemins racines référencés (ex. ["etapes","valider","sortie"]) — sert à la validation.</summary>
    public IEnumerable<IReadOnlyList<string>> Chemins()
    {
        var liste = new List<IReadOnlyList<string>>();
        _racine.Visiter(n =>
        {
            if (n is NoeudChemin c) liste.Add(c.Segments);
        });
        return liste;
    }

    // ── Sémantique des valeurs ──────────────────────────────────────────────

    public static bool EstVrai(JsonNode? v) => v switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        JsonValue val when val.GetValueKind() == JsonValueKind.True => true,
        JsonValue val when val.GetValueKind() == JsonValueKind.False => false,
        JsonValue val when val.GetValueKind() == JsonValueKind.Number => Nombre(val) != 0,
        JsonValue val when val.TryGetValue<string>(out var s) =>
            !(s.Length == 0 || s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0"
              || s.Equals("non", StringComparison.OrdinalIgnoreCase) || s.Equals("null", StringComparison.OrdinalIgnoreCase)),
        _ => true
    };

    public static string EnTexte(JsonNode? v) => v switch
    {
        null => string.Empty,
        JsonValue val when val.TryGetValue<string>(out var s) => s,
        JsonValue val when val.GetValueKind() == JsonValueKind.Number => Nombre(val).ToString(CultureInfo.InvariantCulture),
        JsonValue val when val.GetValueKind() is JsonValueKind.True or JsonValueKind.False => val.GetValueKind() == JsonValueKind.True ? "true" : "false",
        _ => v.ToJsonString()
    };

    internal static double Nombre(JsonNode? v) => v switch
    {
        JsonValue val when val.GetValueKind() == JsonValueKind.Number => Double(val),
        JsonValue val when val.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        JsonValue val when val.GetValueKind() == JsonValueKind.True => 1,
        null => 0,
        _ => double.NaN
    };

    /// <summary>Lecture numérique quel que soit le type .NET porté (int, long, double, decimal, JsonElement).</summary>
    internal static double Double(JsonValue v) =>
        v.TryGetValue<double>(out var d) ? d
        : v.TryGetValue<long>(out var l) ? l
        : v.TryGetValue<int>(out var i) ? i
        : v.TryGetValue<decimal>(out var m) ? (double)m
        : v.TryGetValue<float>(out var f) ? f
        : double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture);

    private static bool EstNumerique(JsonNode? v) =>
        v is JsonValue val && (val.GetValueKind() == JsonValueKind.Number
                               || (val.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)));

    internal static bool Egal(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        var ka = a.GetValueKind();
        var kb = b.GetValueKind();
        if (ka == JsonValueKind.Number || kb == JsonValueKind.Number)
            return EstNumerique(a) && EstNumerique(b) && Nombre(a) == Nombre(b);
        if (ka is JsonValueKind.True or JsonValueKind.False || kb is JsonValueKind.True or JsonValueKind.False)
            return string.Equals(EnTexte(a), EnTexte(b), StringComparison.OrdinalIgnoreCase);
        if (ka == JsonValueKind.String && kb == JsonValueKind.String)
            return string.Equals(EnTexte(a), EnTexte(b), StringComparison.Ordinal);
        return JsonNode.DeepEquals(a, b);
    }

    internal static int Comparer(JsonNode? a, JsonNode? b)
    {
        if (EstNumerique(a) && EstNumerique(b)) return Nombre(a).CompareTo(Nombre(b));
        return string.CompareOrdinal(EnTexte(a), EnTexte(b));
    }

    internal static JsonNode? Valeur(object? v) => v switch
    {
        null => null,
        JsonNode n => n.DeepClone(),
        bool b => JsonValue.Create(b),
        string s => JsonValue.Create(s),
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => JsonValue.Create((long)d),
        double d => JsonValue.Create(d),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        _ => JsonValue.Create(v.ToString())
    };

    // ── Arbre syntaxique ────────────────────────────────────────────────────

    private abstract class Noeud
    {
        public abstract JsonNode? Evaluer(JsonObject ctx);
        public virtual void Visiter(Action<Noeud> action) => action(this);
    }

    private sealed class NoeudLitteral(JsonNode? valeur) : Noeud
    {
        public override JsonNode? Evaluer(JsonObject ctx) => valeur?.DeepClone();
    }

    private sealed class NoeudChemin(List<string> segments) : Noeud
    {
        public IReadOnlyList<string> Segments => segments;

        public override JsonNode? Evaluer(JsonObject ctx)
        {
            JsonNode? courant = ctx;
            foreach (var s in segments)
            {
                courant = Membre(courant, s);
                if (courant is null) return null;
            }
            return courant?.DeepClone();
        }
    }

    private sealed class NoeudMembre(Noeud cible, string nom) : Noeud
    {
        public override JsonNode? Evaluer(JsonObject ctx) => Membre(cible.Evaluer(ctx), nom)?.DeepClone();
        public override void Visiter(Action<Noeud> action) { action(this); cible.Visiter(action); }
    }

    private sealed class NoeudIndex(Noeud cible, Noeud index) : Noeud
    {
        public override JsonNode? Evaluer(JsonObject ctx)
        {
            var c = cible.Evaluer(ctx);
            var i = index.Evaluer(ctx);
            if (c is JsonArray a && EstNumerique(i))
            {
                var n = (int)Nombre(i);
                if (n < 0) n += a.Count;
                return n >= 0 && n < a.Count ? a[n]?.DeepClone() : null;
            }
            return Membre(c, EnTexte(i))?.DeepClone();
        }

        public override void Visiter(Action<Noeud> action) { action(this); cible.Visiter(action); index.Visiter(action); }
    }

    private sealed class NoeudUnaire(string op, Noeud operande) : Noeud
    {
        public override JsonNode? Evaluer(JsonObject ctx)
        {
            var v = operande.Evaluer(ctx);
            return op == "!" ? JsonValue.Create(!EstVrai(v)) : Valeur(-Nombre(v));
        }

        public override void Visiter(Action<Noeud> action) { action(this); operande.Visiter(action); }
    }

    private sealed class NoeudBinaire(string op, Noeud gauche, Noeud droite) : Noeud
    {
        public override JsonNode? Evaluer(JsonObject ctx)
        {
            // Court-circuit : && et || retournent la valeur de l'opérande (comme en JavaScript),
            // ce qui permet « entrees.x || 'défaut' ».
            if (op == "&&")
            {
                var g = gauche.Evaluer(ctx);
                return EstVrai(g) ? droite.Evaluer(ctx) : g;
            }
            if (op == "||")
            {
                var g = gauche.Evaluer(ctx);
                return EstVrai(g) ? g : droite.Evaluer(ctx);
            }
            if (op == "??")
                return gauche.Evaluer(ctx) ?? droite.Evaluer(ctx);

            var a = gauche.Evaluer(ctx);
            var b = droite.Evaluer(ctx);
            return op switch
            {
                "==" => JsonValue.Create(Egal(a, b)),
                "!=" => JsonValue.Create(!Egal(a, b)),
                ">" => JsonValue.Create(Comparer(a, b) > 0),
                "<" => JsonValue.Create(Comparer(a, b) < 0),
                ">=" => JsonValue.Create(Comparer(a, b) >= 0),
                "<=" => JsonValue.Create(Comparer(a, b) <= 0),
                "+" when EstNumerique(a) && EstNumerique(b) && !(a is JsonValue va && va.GetValueKind() == JsonValueKind.String) => Valeur(Nombre(a) + Nombre(b)),
                "+" => JsonValue.Create(EnTexte(a) + EnTexte(b)),
                "-" => Valeur(Nombre(a) - Nombre(b)),
                "*" => Valeur(Nombre(a) * Nombre(b)),
                "/" => Nombre(b) == 0 ? throw new ErreurExpression("Division par zéro.") : Valeur(Nombre(a) / Nombre(b)),
                "%" => Nombre(b) == 0 ? throw new ErreurExpression("Division par zéro.") : Valeur(Nombre(a) % Nombre(b)),
                _ => throw new ErreurExpression($"Opérateur inconnu « {op} ».")
            };
        }

        public override void Visiter(Action<Noeud> action) { action(this); gauche.Visiter(action); droite.Visiter(action); }
    }

    private sealed class NoeudTernaire(Noeud condition, Noeud alors, Noeud sinon) : Noeud
    {
        public override JsonNode? Evaluer(JsonObject ctx) =>
            EstVrai(condition.Evaluer(ctx)) ? alors.Evaluer(ctx) : sinon.Evaluer(ctx);

        public override void Visiter(Action<Noeud> action)
        {
            action(this); condition.Visiter(action); alors.Visiter(action); sinon.Visiter(action);
        }
    }

    private sealed class NoeudAppel(string nom, List<Noeud> arguments) : Noeud
    {
        private readonly Func<JsonNode?[], JsonNode?> _fonction =
            Fonctions.Trouver(nom) ?? throw new ErreurExpression($"Fonction inconnue « {nom} ». Disponibles : {string.Join(", ", Fonctions.Noms)}.");

        public override JsonNode? Evaluer(JsonObject ctx) =>
            _fonction(arguments.Select(a => a.Evaluer(ctx)).ToArray());

        public override void Visiter(Action<Noeud> action)
        {
            action(this);
            foreach (var a in arguments) a.Visiter(action);
        }
    }

    private static JsonNode? Membre(JsonNode? cible, string nom)
    {
        switch (cible)
        {
            case JsonObject o:
                if (o.TryGetPropertyValue(nom, out var v)) return v;
                foreach (var (cle, valeur) in o)
                    if (string.Equals(cle, nom, StringComparison.OrdinalIgnoreCase)) return valeur;
                return null;
            case JsonArray a when nom is "longueur" or "length" or "count":
                return JsonValue.Create(a.Count);
            case JsonArray a when int.TryParse(nom, out var i):
                return i >= 0 && i < a.Count ? a[i] : null;
            default:
                return null;
        }
    }

    // ── Analyseur (descente récursive) ──────────────────────────────────────

    private sealed class Analyseur(string source)
    {
        private readonly List<Jeton> _jetons = Tokeniser(source);
        private int _pos;

        private Jeton Courant => _jetons[_pos];

        public Noeud AnalyserTout()
        {
            if (_jetons.Count == 1) throw new ErreurExpression("Expression vide.");
            var n = Ternaire();
            if (Courant.Type != TypeJeton.Fin)
                throw new ErreurExpression($"Jeton inattendu « {Courant.Texte} » à la position {Courant.Position + 1} dans « {source} ».");
            return n;
        }

        private bool Accepter(params string[] ops)
        {
            if (Courant.Type is TypeJeton.Operateur or TypeJeton.Identifiant && ops.Contains(Courant.Texte))
            {
                _pos++;
                return true;
            }
            return false;
        }

        private void Exiger(string op)
        {
            if (!Accepter(op))
                throw new ErreurExpression($"« {op} » attendu à la position {Courant.Position + 1} dans « {source} ».");
        }

        private Noeud Ternaire()
        {
            var c = Ou();
            if (Accepter("?"))
            {
                var alors = Ternaire();
                Exiger(":");
                var sinon = Ternaire();
                return new NoeudTernaire(c, alors, sinon);
            }
            return c;
        }

        private Noeud Ou()
        {
            var g = Et();
            while (true)
            {
                if (Accepter("||", "or", "ou")) g = new NoeudBinaire("||", g, Et());
                else if (Accepter("??")) g = new NoeudBinaire("??", g, Et());
                else return g;
            }
        }

        private Noeud Et()
        {
            var g = Non();
            while (Accepter("&&", "and", "et")) g = new NoeudBinaire("&&", g, Non());
            return g;
        }

        private Noeud Non() => Accepter("!", "not", "non") ? new NoeudUnaire("!", Non()) : Comparaison();

        private Noeud Comparaison()
        {
            var g = Addition();
            var op = Courant.Texte;
            if (Accepter("==", "!=", ">", "<", ">=", "<=")) return new NoeudBinaire(op, g, Addition());
            return g;
        }

        private Noeud Addition()
        {
            var g = Multiplication();
            while (true)
            {
                var op = Courant.Texte;
                if (Accepter("+", "-")) g = new NoeudBinaire(op, g, Multiplication());
                else return g;
            }
        }

        private Noeud Multiplication()
        {
            var g = Unaire();
            while (true)
            {
                var op = Courant.Texte;
                if (Accepter("*", "/", "%")) g = new NoeudBinaire(op, g, Unaire());
                else return g;
            }
        }

        private Noeud Unaire() => Accepter("-") ? new NoeudUnaire("-", Unaire()) : Postfixe();

        private Noeud Postfixe()
        {
            var n = Primaire();
            while (true)
            {
                if (Accepter("."))
                {
                    var j = Courant;
                    if (j.Type is not (TypeJeton.Identifiant or TypeJeton.Nombre))
                        throw new ErreurExpression($"Nom de propriété attendu après « . » à la position {j.Position + 1} dans « {source} ».");
                    _pos++;
                    n = n is NoeudChemin c ? new NoeudChemin([.. c.Segments, j.Texte]) : new NoeudMembre(n, j.Texte);
                }
                else if (Accepter("["))
                {
                    var index = Ternaire();
                    Exiger("]");
                    n = new NoeudIndex(n, index);
                }
                else return n;
            }
        }

        private Noeud Primaire()
        {
            var j = Courant;
            switch (j.Type)
            {
                case TypeJeton.Nombre:
                    _pos++;
                    return new NoeudLitteral(Valeur(double.Parse(j.Texte, CultureInfo.InvariantCulture)));
                case TypeJeton.Texte:
                    _pos++;
                    return new NoeudLitteral(JsonValue.Create(j.Texte));
                case TypeJeton.Identifiant:
                    _pos++;
                    switch (j.Texte)
                    {
                        case "true" or "vrai": return new NoeudLitteral(JsonValue.Create(true));
                        case "false" or "faux": return new NoeudLitteral(JsonValue.Create(false));
                        case "null" or "nul": return new NoeudLitteral(null);
                    }
                    if (Accepter("("))
                    {
                        var args = new List<Noeud>();
                        if (!Accepter(")"))
                        {
                            do args.Add(Ternaire()); while (Accepter(","));
                            Exiger(")");
                        }
                        return new NoeudAppel(j.Texte, args);
                    }
                    return new NoeudChemin([j.Texte]);
                case TypeJeton.Operateur when j.Texte == "(":
                    _pos++;
                    var n = Ternaire();
                    Exiger(")");
                    return n;
                default:
                    throw new ErreurExpression(j.Type == TypeJeton.Fin
                        ? $"Expression incomplète : « {source} »."
                        : $"Jeton inattendu « {j.Texte} » à la position {j.Position + 1} dans « {source} ».");
            }
        }
    }

    private enum TypeJeton { Nombre, Texte, Identifiant, Operateur, Fin }

    private readonly record struct Jeton(TypeJeton Type, string Texte, int Position);

    private static readonly string[] Operateurs =
        ["==", "!=", ">=", "<=", "&&", "||", "??", ">", "<", "!", "+", "-", "*", "/", "%", "(", ")", "[", "]", ".", ",", "?", ":"];

    private static List<Jeton> Tokeniser(string s)
    {
        var jetons = new List<Jeton>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsDigit(c))
            {
                var debut = i;
                while (i < s.Length && (char.IsDigit(s[i]) || (s[i] == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))) i++;
                jetons.Add(new Jeton(TypeJeton.Nombre, s[debut..i], debut));
                continue;
            }

            if (c is '\'' or '"')
            {
                var debut = i++;
                var sb = new StringBuilder();
                while (i < s.Length && s[i] != c)
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                if (i >= s.Length) throw new ErreurExpression($"Texte non terminé à la position {debut + 1} dans « {s} ».");
                i++;
                jetons.Add(new Jeton(TypeJeton.Texte, sb.ToString(), debut));
                continue;
            }

            if (char.IsLetter(c) || c is '_' or '$')
            {
                var debut = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '$' || EstTiretInterne(s, i))) i++;
                jetons.Add(new Jeton(TypeJeton.Identifiant, s[debut..i], debut));
                continue;
            }

            var op = Operateurs.FirstOrDefault(o => string.CompareOrdinal(s, i, o, 0, o.Length) == 0)
                     ?? throw new ErreurExpression($"Caractère inattendu « {c} » à la position {i + 1} dans « {s} ».");
            jetons.Add(new Jeton(TypeJeton.Operateur, op, i));
            i += op.Length;
        }
        jetons.Add(new Jeton(TypeJeton.Fin, string.Empty, s.Length));
        return jetons;
    }

    // Les identifiants peuvent contenir des tirets (ex. etapes.valider-dossier.sortie) :
    // un tiret collé entre deux caractères alphanumériques fait partie du nom.
    // Pour soustraire, on entoure l'opérateur d'espaces : « a - b ».
    private static bool EstTiretInterne(string s, int i) =>
        s[i] == '-' && i + 1 < s.Length && char.IsLetterOrDigit(s[i + 1]);
}
