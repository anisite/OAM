namespace OIM.Moteur.Definitions;

public sealed record TypeEtape(
    string Nom,
    string Libelle,
    string Description,
    string[] Requises,
    string[] Optionnelles,
    bool AccepteRetry);

/// <summary>Types d'étapes reconnus par l'interpréteur et propriétés admises pour chacun.</summary>
public static class CatalogueEtapes
{
    public const string Http = "http";
    public const string AttendreEvenement = "attendreEvenement";
    public const string Courriel = "courriel";
    public const string Delai = "delai";
    public const string Decision = "decision";
    public const string Definir = "definir";
    public const string SousProcessus = "sousProcessus";
    public const string Reponse = "reponse";

    // Étapes de transmission de documents (reprises d'ECS25A). Les cinq premières appellent un service
    // configuré (Oim:Services:<type>) avec leurs propriétés résolues; boiteGenerique route et envoie un courriel.
    public const string ChargerDocuments = "chargerDocuments";
    public const string ApparierGdi = "apparierGdi";
    public const string ValiderDossierAnterieur = "validerDossierAnterieur";
    public const string GenererPageGarde = "genererPageGarde";
    public const string DeposerGed = "deposerGed";
    public const string BoiteGenerique = "boiteGenerique";

    /// <summary>Types exécutés par un appel au service configuré (et simulés par mock en test).</summary>
    public static readonly IReadOnlySet<string> TypesService = new HashSet<string>(StringComparer.Ordinal)
        { ChargerDocuments, ApparierGdi, ValiderDossierAnterieur, GenererPageGarde, DeposerGed };

    public static readonly IReadOnlyList<TypeEtape> Types =
    [
        new(Http, "Appel HTTP", "Appel d'un service via un gabarit YamlHttpClient.",
            ["requete"], ["donnees"], true),
        new(AttendreEvenement, "Attente d'événement", "Attend un événement externe, avec délai optionnel.",
            ["evenement"], ["delai", "siDelaiExpire"], false),
        new(Courriel, "Courriel", "Envoi d'un courriel à partir d'un gabarit.",
            ["gabarit"], ["a", "cc", "cci", "donnees", "langue"], true),
        new(Delai, "Délai", "Pause durable pendant une durée ou jusqu'à une date.",
            [], ["duree", "jusqua"], false),
        new(Decision, "Décision", "Aiguillage selon les conditions de « suivant ».",
            [], [], false),
        new(Definir, "Variables", "Calcule des valeurs et les conserve dans « variables ».",
            ["variables"], [], false),
        new(SousProcessus, "Sous-processus", "Démarre un autre processus déployé et attend sa fin.",
            ["processus"], ["version", "entrees"], true),
        new(Reponse, "Réponse à l'appelant", "Publie la réponse HTTP d'un démarrage synchrone (?attendre=) ; le processus continue.",
            [], ["statutHttp", "corps"], false),
        new(ChargerDocuments, "Charger les documents", "Rassemble les documents de la soumission (FRW, fichiers bruts, dépôt ECS) en références.",
            [], ["documents", "fichiersBruts", "piecesJointes", "fichiersFrw", "pjDepotEcs", "langue"], true),
        new(ApparierGdi, "Apparier au GDI", "Recherche l'individu au GDI (seuils de comparaison), le crée ou le met à jour au besoin.",
            ["identite"], ["comparaisons", "seuilGdi", "seuilVirq", "creer", "modifier"], true),
        new(ValiderDossierAnterieur, "Dossier antérieur", "Obtient le dossier antérieur de l'individu : ASF allégé, présence INE, code secteur emploi.",
            ["noGdi"], [], true),
        new(GenererPageGarde, "Page de garde", "Génère la page de garde PDF (gabarit GCO) et la fusionne au besoin.",
            ["gabarit"], ["titre", "sousTitre", "identite", "renseignementAdditionnel", "documents", "fusionner", "nomFichier"], true),
        new(DeposerGed, "Déposer à la GED", "Dépose les documents à la GED selon des envois filtrés (type, sujet, lignes d'affaires).",
            ["envois", "documents"], ["individu", "langue", "informationsSupplementaires"], true),
        new(BoiteGenerique, "Boîte générique", "Choisit la boîte générique (conditions, puis table BSQ) et lui envoie un courriel.",
            ["blocs", "gabarit"], ["donnees", "langue"], true)
    ];

    public static TypeEtape? Trouver(string type) =>
        Types.FirstOrDefault(t => string.Equals(t.Nom, type, StringComparison.Ordinal));

    public static readonly IReadOnlyList<string> TypesEntree = ["string", "int", "number", "bool", "date", "object", "array"];
}
