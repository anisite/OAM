using System.Text.Json.Nodes;
using OIM.Moteur.Definitions;

namespace OIM.Moteur.Tests;

/// <summary>Action du scénario : attendre qu'une étape soit en attente, puis agir comme le monde extérieur.</summary>
public sealed record ActionScenario(string Attendre, JsonObject? Evenements, bool DelaiExpire);

/// <summary>
/// Cas de test métier (<c>tests/&lt;nom&gt;.yml</c> du paquet) :
/// <code>
/// nom: Approbation après une relance
/// entrees: { dossierId: 42, courriel: citoyen@exemple.com }
/// mocks:                                   # par id d'étape (liste = un mock par passage)
///   valider: { statut: 200, corps: { valide: true, numero: D-42 } }
/// scenario:
///   - attendre: approbation
///     delaiExpire: true                    # simule l'expiration du délai
///   - attendre: approbation
///     evenement: { decision: { approuve: true } }
/// attendu:                                 # comparaison partielle : seules ces clés sont vérifiées
///   statut: Completed
///   parcours: [valider, accuser, approbation, relance, approbation, confirmer]
///   reponse: { statutHttp: 201 }
///   courriels: [ { gabarit: gabarits/relance-approbateur.yml }, { a: [citoyen@exemple.com] } ]
/// </code>
/// </summary>
public sealed class CasTest
{
    public required string Fichier { get; init; }
    public required string Nom { get; init; }
    public string? Description { get; init; }
    public JsonObject Entrees { get; init; } = [];
    public JsonObject Mocks { get; init; } = [];
    public IReadOnlyList<ActionScenario> Scenario { get; init; } = [];
    public JsonObject Attendu { get; init; } = [];
    public TimeSpan DelaiMax { get; init; } = TimeSpan.FromSeconds(60);

    public static bool EstCasTest(string chemin) =>
        chemin.StartsWith("tests/", StringComparison.OrdinalIgnoreCase)
        && (chemin.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || chemin.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<(string Chemin, string Contenu)> Trouver(PaquetDefinition paquet) =>
        paquet.Fichiers.Where(f => EstCasTest(f.Key)).OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase).Select(f => (f.Key, f.Value));

    /// <exception cref="FormatException">Fichier illisible ou incomplet.</exception>
    public static CasTest Lire(string chemin, string contenu)
    {
        JsonObject racine;
        try
        {
            racine = YamlJson.Lire(contenu) as JsonObject ?? throw new FormatException("le fichier doit être un objet YAML.");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new FormatException($"YAML invalide (ligne {ex.Start.Line}) : {ex.InnerException?.Message ?? ex.Message}");
        }

        var scenario = new List<ActionScenario>();
        if (racine["scenario"] is JsonArray actions)
            foreach (var (action, i) in actions.Select((a, i) => (a, i + 1)))
            {
                if (action is not JsonObject a || LecteurDefinition.Texte(a, "attendre") is not { } etape)
                    throw new FormatException($"scenario[{i}] : « attendre: <id d'étape> » est obligatoire.");
                var expire = a["delaiExpire"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
                var evenements = a["evenement"] as JsonObject ?? a["evenements"] as JsonObject;
                if (!expire && evenements is null)
                    throw new FormatException($"scenario[{i}] : préciser « evenement: {{ <nom>: <données> }} » ou « delaiExpire: true ».");
                scenario.Add(new ActionScenario(etape, evenements?.DeepClone().AsObject(), expire));
            }
        else if (racine["scenario"] is not null)
            throw new FormatException("« scenario » doit être une liste.");

        var delaiMax = TimeSpan.FromSeconds(60);
        if (LecteurDefinition.Texte(racine, "delaiMax") is { } d && !Duree.TryLire(d, out delaiMax))
            throw new FormatException($"delaiMax « {d} » invalide.");

        return new CasTest
        {
            Fichier = chemin,
            Nom = LecteurDefinition.Texte(racine, "nom") ?? Path.GetFileNameWithoutExtension(chemin),
            Description = LecteurDefinition.Texte(racine, "description"),
            Entrees = racine["entrees"]?.DeepClone() as JsonObject ?? [],
            Mocks = racine["mocks"]?.DeepClone() as JsonObject ?? [],
            Scenario = scenario,
            Attendu = racine["attendu"]?.DeepClone() as JsonObject
                      ?? throw new FormatException("« attendu » est obligatoire (au moins « statut »)."),
            DelaiMax = delaiMax
        };
    }
}
