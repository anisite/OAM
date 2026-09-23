using System.Text.Json.Nodes;
using OIM.Moteur.Expressions;
using OIM.Moteur.Orchestration.Activites;

namespace OIM.Moteur.Definitions;

/// <summary>
/// Validation complète d'un paquet avant déploiement : structure, types d'étapes,
/// transitions, expressions, durées et fichiers annexes référencés.
/// </summary>
public static class ValidateurDefinition
{
    private static readonly HashSet<string> RacinesContexte =
        ["entrees", "etapes", "evenement", "variables", "erreur", "instance", "maintenant"];

    public static ResultatLecture Valider(PaquetDefinition paquet)
    {
        var resultat = LecteurDefinition.Lire(paquet.Yaml);
        if (resultat.Definition is not { } def) return resultat;
        var d = resultat.Diagnostics;

        foreach (var entree in def.Entrees)
            if (!CatalogueEtapes.TypesEntree.Contains(entree.Type))
                d.Add(Err($"Entrée « {entree.Nom} » : type « {entree.Type} » inconnu ({string.Join(", ", CatalogueEtapes.TypesEntree)})."));

        if (def.Etapes.Count == 0)
        {
            d.Add(Err("Le processus doit contenir au moins une étape."));
            return resultat;
        }

        foreach (var doublon in def.Etapes.GroupBy(e => e.Id).Where(g => g.Count() > 1))
            d.Add(Err($"L'identifiant d'étape « {doublon.Key} » est utilisé {doublon.Count()} fois.", doublon.Key));

        var ids = def.Etapes.Select(e => e.Id).ToHashSet();

        foreach (var etape in def.Etapes)
            ValiderEtape(etape, ids, paquet, d);

        // Étapes inatteignables depuis l'étape initiale
        var atteintes = new HashSet<string>();
        var pile = new Stack<string>([def.Etapes[0].Id]);
        while (pile.TryPop(out var id))
        {
            if (!atteintes.Add(id) || def.TrouverEtape(id) is not { } e) continue;
            foreach (var (cible, _) in e.Transitions()) pile.Push(cible);
        }
        foreach (var e in def.Etapes.Where(e => !atteintes.Contains(e.Id)))
            d.Add(Avert($"L'étape « {e.Id} » n'est jamais atteinte depuis « {def.Etapes[0].Id} ».", e.Id));

        if (!def.Etapes.Any(e => e.Fin || e.Suivant.Count == 0))
            d.Add(Avert("Aucune étape de fin (fin: true) : le processus ne se terminera jamais normalement."));

        foreach (var (chemin, contenu) in Tests.CasTest.Trouver(paquet))
            ValiderCasTest(chemin, contenu, def, d);

        return resultat;
    }

    private static void ValiderEtape(Etape etape, HashSet<string> ids, PaquetDefinition paquet, List<Diagnostic> d)
    {
        var type = CatalogueEtapes.Trouver(etape.Type);
        if (type is null)
        {
            d.Add(Err(etape.Type.Length == 0
                ? "Le type d'étape est obligatoire."
                : $"Type d'étape « {etape.Type} » inconnu. Types disponibles : {string.Join(", ", CatalogueEtapes.Types.Select(t => t.Nom))}.", etape.Id));
            return;
        }

        foreach (var requise in type.Requises)
            if (etape.Valeur(requise) is null)
                d.Add(Err($"La propriété « {requise} » est obligatoire pour une étape « {type.Nom} ».", etape.Id));

        foreach (var (cle, _) in etape.Proprietes)
            if (!type.Requises.Contains(cle) && !type.Optionnelles.Contains(cle))
                d.Add(Avert($"Propriété « {cle} » ignorée pour une étape « {type.Nom} ».", etape.Id));

        if (etape.Retry is not null && !type.AccepteRetry)
            d.Add(Avert($"« retry » n'a pas d'effet sur une étape « {type.Nom} ».", etape.Id));

        // Transitions
        foreach (var (cible, nature) in etape.Transitions())
            if (string.IsNullOrWhiteSpace(cible))
                d.Add(Err($"Une transition « {nature} » n'a pas d'étape cible.", etape.Id));
            else if (!ids.Contains(cible))
                d.Add(Err($"« {nature} » référence l'étape inexistante « {cible} ».", etape.Id));

        if (etape.Fin && etape.Suivant.Count > 0)
            d.Add(Avert("L'étape est marquée « fin » : « suivant » sera ignoré.", etape.Id));

        var sinons = etape.Suivant.Count(b => b.Condition is null);
        if (etape.Suivant.Count > 1)
        {
            if (sinons > 1) d.Add(Err("Une seule branche « sinon » est permise.", etape.Id));
            if (sinons == 1 && etape.Suivant[^1].Condition is not null)
                d.Add(Err("La branche « sinon » doit être la dernière.", etape.Id));
            if (sinons == 0)
                d.Add(Avert("Aucune branche « sinon » : le processus se terminera si aucune condition n'est vraie.", etape.Id));
        }

        if (etape.Type == CatalogueEtapes.Decision && etape.Suivant.Count == 0)
            d.Add(Err("Une étape « decision » doit définir des branches dans « suivant ».", etape.Id));

        // Expressions
        foreach (var b in etape.Suivant.Where(b => b.Condition is not null))
            ValiderExpression(Gabarit.SourceCondition(b.Condition!), etape.Id, ids, d);
        foreach (var texte in new[] { etape.Statut, etape.Message })
        foreach (var expr in Gabarit.Expressions(texte is null ? null : JsonValue.Create(texte)))
            ValiderExpression(expr, etape.Id, ids, d);
        foreach (var expr in Gabarit.Expressions(etape.Proprietes))
            ValiderExpression(expr, etape.Id, ids, d);

        // Validations propres au type
        switch (etape.Type)
        {
            case CatalogueEtapes.Http when etape.Texte("requete") is { } requete:
                ValiderRequete(requete, etape.Id, paquet, d);
                break;

            case CatalogueEtapes.AttendreEvenement:
                if (etape.Texte("delai") is { } delai && !Gabarit.ContientExpression(delai) && !Duree.TryLire(delai, out _))
                    d.Add(Err($"Délai « {delai} » invalide (ex. 5.00:00:00, 30m, P5D).", etape.Id));
                if (etape.Texte("siDelaiExpire") is not null && etape.Texte("delai") is null)
                    d.Add(Avert("« siDelaiExpire » sans « delai » : l'attente n'expirera jamais.", etape.Id));
                break;

            case CatalogueEtapes.Courriel when etape.Texte("gabarit") is { } gabarit && !Gabarit.ContientExpression(gabarit):
                if (paquet.TrouverGabarit(gabarit) is not { } g)
                    d.Add(Err($"Gabarit de courriel « {gabarit} » introuvable dans le paquet (attendu : gabarits/{gabarit}.yml).", etape.Id));
                else
                    ValiderGabaritCourriel(g.Chemin, g.Contenu, etape, d);
                break;

            case CatalogueEtapes.Delai:
                var duree = etape.Texte("duree");
                if (duree is null && etape.Texte("jusqua") is null)
                    d.Add(Err("Une étape « delai » exige « duree » ou « jusqua ».", etape.Id));
                if (duree is not null && !Gabarit.ContientExpression(duree) && !Duree.TryLire(duree, out _))
                    d.Add(Err($"Durée « {duree} » invalide.", etape.Id));
                break;

            case CatalogueEtapes.Reponse when etape.Texte("statutHttp") is { } code && !Gabarit.ContientExpression(code):
                if (!int.TryParse(code, out var n) || n is < 100 or > 599)
                    d.Add(Err($"statutHttp « {code} » doit être un code HTTP (100 à 599).", etape.Id));
                break;

            case CatalogueEtapes.Definir when etape.Valeur("variables") is not JsonObject:
                d.Add(Err("« variables » doit être un dictionnaire nom → valeur.", etape.Id));
                break;
        }
    }

    private static void ValiderRequete(string reference, string etape, PaquetDefinition paquet, List<Diagnostic> d)
    {
        if (paquet.TrouverRequete(reference) is not { } fichier)
        {
            d.Add(Err($"Gabarit de requête « {reference} » introuvable dans le paquet.", etape));
            return;
        }
        try
        {
            RequeteHttp.ChoisirCle(fichier.Contenu, reference);
        }
        catch (Exception ex)
        {
            d.Add(Err($"Gabarit « {fichier.Chemin} » : {ex.Message}", etape));
        }
    }

    private static void ValiderGabaritCourriel(string chemin, string contenu, Etape etape, List<Diagnostic> d)
    {
        try
        {
            if (YamlJson.Lire(contenu) is not JsonObject o)
            {
                d.Add(Err($"Gabarit « {chemin} » : le fichier doit contenir sujet et corps.", etape.Id));
                return;
            }
            if (o["sujet"] is null) d.Add(Err($"Gabarit « {chemin} » : « sujet » manquant.", etape.Id));
            if (o["corps"] is null) d.Add(Err($"Gabarit « {chemin} » : « corps » manquant.", etape.Id));
            if (o["a"] is null && etape.Valeur("a") is null)
                d.Add(Err($"Aucun destinataire : ni « a » dans l'étape, ni dans le gabarit « {chemin} ».", etape.Id));
        }
        catch (Exception ex)
        {
            d.Add(Err($"Gabarit « {chemin} » illisible : {ex.Message}", etape.Id));
        }
    }

    private static void ValiderExpression(string source, string etape, HashSet<string> ids, List<Diagnostic> d)
    {
        try
        {
            var expr = Expression.Analyser(source);
            foreach (var chemin in expr.Chemins())
            {
                if (!RacinesContexte.Contains(chemin[0]))
                    d.Add(Avert($"« {chemin[0]} » n'est pas une variable connue ({string.Join(", ", RacinesContexte)}) dans « {source} ».", etape));
                else if (chemin[0] == "etapes" && chemin.Count > 1 && !ids.Contains(chemin[1]))
                    d.Add(Err($"L'expression « {source} » référence l'étape inexistante « {chemin[1]} ».", etape));
            }
        }
        catch (ErreurExpression ex)
        {
            d.Add(Err($"Expression invalide : {ex.Message}", etape));
        }
    }

    /// <summary>Cohérence d'un cas de test avec la définition (les écarts de résultat sont vus à l'exécution).</summary>
    private static void ValiderCasTest(string chemin, string contenu, DefinitionProcessus def, List<Diagnostic> d)
    {
        Tests.CasTest cas;
        try
        {
            cas = Tests.CasTest.Lire(chemin, contenu);
        }
        catch (FormatException ex)
        {
            d.Add(Err($"Test « {chemin} » : {ex.Message}"));
            return;
        }

        foreach (var (etape, _) in cas.Mocks)
            if (def.TrouverEtape(etape) is not { } e)
                d.Add(Err($"Test « {chemin} » : mock pour l'étape inexistante « {etape} »."));
            else if (e.Type is not (CatalogueEtapes.Http or CatalogueEtapes.SousProcessus))
                d.Add(Avert($"Test « {chemin} » : mock inutile pour « {etape} » (seules les étapes http et sousProcessus sont simulées par mock)."));

        foreach (var action in cas.Scenario)
            if (def.TrouverEtape(action.Attendre) is not { } e)
                d.Add(Err($"Test « {chemin} » : le scénario attend l'étape inexistante « {action.Attendre} »."));
            else if (e.Type != CatalogueEtapes.AttendreEvenement)
                d.Add(Err($"Test « {chemin} » : « {action.Attendre} » n'est pas une étape attendreEvenement."));

        foreach (var e in def.Etapes.Where(e => e.Type is CatalogueEtapes.Http or CatalogueEtapes.SousProcessus && !cas.Mocks.ContainsKey(e.Id)))
            d.Add(Avert($"Test « {chemin} » : pas de mock pour « {e.Id} » : le cas échouera s'il passe par cette étape."));
    }

    private static Diagnostic Err(string m, string? etape = null) => new(Diagnostic.Erreur, m, etape);
    private static Diagnostic Avert(string m, string? etape = null) => new(Diagnostic.Avertissement, m, etape);
}
