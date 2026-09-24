using System.Text.Json.Nodes;

namespace OIM.Moteur.Orchestration;

public static class NomsActivites
{
    public const string ChargerDefinition = "oim.chargerDefinition";
    public const string Http = "oim.http";
    public const string Courriel = "oim.courriel";
    public const string Reponse = "oim.reponse";

    /// <summary>Étapes dédiées (chargerDocuments, apparierGdi…) : appel du service configuré.</summary>
    public const string Service = "oim.service";

    /// <summary>Étape boiteGenerique : adresse de la boîte retenue (bloc ou table BSQ).</summary>
    public const string ResoudreBoite = "oim.resoudreBoite";

    /// <summary>Événement qui simule l'expiration d'un délai d'attente (mode test uniquement).</summary>
    public const string ExpirationSimulee = "oim:delaiExpire";
}

/// <summary>Entrée d'une instance. Le nom d'orchestration DurableTask est l'id du processus.</summary>
public sealed class EntreeOrchestration
{
    /// <summary>
    /// Version du comportement du moteur au démarrage de l'instance. DurableTask rejoue l'historique
    /// des instances en cours avec le code courant : tout changement qui ajoute ou retire une
    /// activité, une minuterie ou un sous-processus doit être conditionné à cette version, sinon les
    /// instances démarrées avant le changement échouent (orchestration non déterministe).
    /// <list type="bullet">
    ///   <item>1 : version initiale.</item>
    ///   <item>2 : l'étape « reponse » trace une activité oim.reponse dans l'historique.</item>
    ///   <item>(sans changement de version) nouveaux types d'étapes chargerDocuments, apparierGdi,
    ///   validerDossierAnterieur, genererPageGarde, deposerGed (activité oim.service) et boiteGenerique
    ///   (oim.resoudreBoite + oim.courriel) : aucune instance existante ne les utilise, leur historique est inchangé.</item>
    /// </list>
    /// </summary>
    public const int VersionMoteurCourante = 2;

    public required string DefinitionId { get; init; }
    public required int Version { get; init; }
    public JsonObject Entrees { get; init; } = [];

    /// <summary>Absente des instances démarrées avant son introduction : vaut alors 1.</summary>
    public int VersionMoteur { get; init; } = VersionMoteurCourante;

    /// <summary>
    /// Mode test (tests métier) : <c>{ "mocks": { "&lt;etape&gt;": … } }</c>. Aucun appel externe,
    /// aucune minuterie réelle; voir <see cref="OrchestrationProcessus"/>.
    /// </summary>
    public JsonObject? Test { get; init; }

    public JsonObject VersJson() => new()
    {
        ["definitionId"] = DefinitionId,
        ["version"] = Version,
        ["moteur"] = VersionMoteur,
        ["entrees"] = Entrees.DeepClone(),
        ["test"] = Test?.DeepClone()
    };

    public static EntreeOrchestration Lire(string? texte)
    {
        var o = Json.Lire(texte) as JsonObject
                ?? throw new ErreurProcessus("Entrée d'orchestration invalide : objet JSON attendu.");
        return new EntreeOrchestration
        {
            DefinitionId = o["definitionId"]?.GetValue<string>() ?? throw new ErreurProcessus("definitionId manquant."),
            Version = o["version"]?.GetValue<int>() ?? throw new ErreurProcessus("version manquante."),
            VersionMoteur = o["moteur"]?.GetValue<int>() ?? 1,
            Test = o["test"]?.DeepClone() as JsonObject,
            Entrees = o["entrees"]?.DeepClone() as JsonObject ?? []
        };
    }
}

/// <summary>
/// Statut personnalisé DurableTask (CustomStatus) : c'est ce que le tableau de bord
/// et les applications clientes lisent pour suivre l'avancement métier.
/// </summary>
public sealed class StatutProcessus
{
    public string? Etape { get; set; }
    public string? TypeEtape { get; set; }
    public string? Statut { get; set; }
    public string? Message { get; set; }
    public AttenteProcessus? Attente { get; set; }
    public string? Erreur { get; set; }
    public int Transitions { get; set; }
    public List<string> Parcours { get; set; } = [];
    public DateTime? MiseAJour { get; set; }

    /// <summary>Réponse publiée par une étape « reponse » (démarrage synchrone).</summary>
    public ReponseProcessus? Reponse { get; set; }
}

public sealed class ReponseProcessus
{
    public int StatutHttp { get; set; } = 200;
    public JsonNode? Corps { get; set; }
    public string? Etape { get; set; }
    public DateTime EmiseLe { get; set; }
}

public sealed class AttenteProcessus
{
    /// <summary>Nom de l'événement attendu (null pour une étape « delai »).</summary>
    public string? Evenement { get; set; }
    public DateTime? Echeance { get; set; }
}

/// <summary>Erreur fonctionnelle du processus (délai expiré, expression invalide, définition absente…).</summary>
public sealed class ErreurProcessus(string message) : Exception(message);
