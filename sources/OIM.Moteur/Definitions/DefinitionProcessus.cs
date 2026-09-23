using System.Text.Json.Nodes;

namespace OIM.Moteur.Definitions;

/// <summary>
/// Définition d'un processus telle que décrite dans le fichier YAML déployé.
/// Le modèle est immuable une fois lu : une version déployée ne change jamais,
/// ce qui garantit le déterminisme des orchestrations lors de la relecture.
/// </summary>
public sealed class DefinitionProcessus
{
    public required string Id { get; init; }
    public string? Nom { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<ParametreEntree> Entrees { get; init; } = [];
    public IReadOnlyList<Etape> Etapes { get; init; } = [];

    /// <summary>Garde-fou contre les boucles infinies (ex. relance ↔ approbation).</summary>
    public int TransitionsMax { get; init; } = 1000;

    /// <summary>L'étape de départ est la première étape déclarée.</summary>
    public Etape? EtapeInitiale => Etapes.Count > 0 ? Etapes[0] : null;

    public Etape? TrouverEtape(string id) =>
        Etapes.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
}

public sealed class ParametreEntree
{
    public required string Nom { get; init; }
    public string Type { get; init; } = "string";
    public bool Requis { get; init; }
    public JsonNode? Defaut { get; init; }
    public string? Libelle { get; init; }
    public string? Description { get; init; }
}

public sealed class Etape
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string? Description { get; init; }

    /// <summary>Statut métier affiché (statut personnalisé DurableTask) à l'entrée dans l'étape.</summary>
    public string? Statut { get; init; }

    /// <summary>Message retourné au client, évalué après l'exécution de l'étape.</summary>
    public string? Message { get; init; }

    public bool Fin { get; init; }
    public IReadOnlyList<Branche> Suivant { get; init; } = [];
    public string? SiErreur { get; init; }
    public PolitiqueReprise? Retry { get; init; }

    /// <summary>Propriétés propres au type d'étape (requete, donnees, evenement, delai, gabarit…).</summary>
    public JsonObject Proprietes { get; init; } = [];

    public string? Texte(string propriete) => LecteurDefinition.Texte(Proprietes, propriete);

    public JsonNode? Valeur(string propriete) =>
        Proprietes.TryGetPropertyValue(propriete, out var n) ? n : null;

    /// <summary>Toutes les étapes cibles possibles (suivant, siErreur, siDelaiExpire).</summary>
    public IEnumerable<(string Cible, string Nature)> Transitions()
    {
        foreach (var b in Suivant)
            yield return (b.Aller, b.Condition is null ? (Suivant.Count > 1 ? "sinon" : "suivant") : "si");
        if (SiErreur is not null) yield return (SiErreur, "siErreur");
        if (Texte("siDelaiExpire") is { } expire) yield return (expire, "siDelaiExpire");
    }
}

/// <summary>
/// Transition vers une étape. <see cref="Condition"/> est null pour une transition
/// inconditionnelle (<c>suivant: x</c>) ou pour la branche <c>sinon</c>.
/// </summary>
public sealed record Branche(string? Condition, string Aller);

/// <summary>Politique de reprise d'une activité (traduite en RetryOptions DurableTask).</summary>
public sealed record PolitiqueReprise(int Tentatives, TimeSpan Delai, double Backoff, TimeSpan? DelaiMax);
