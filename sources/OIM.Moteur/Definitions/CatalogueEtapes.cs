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

    public static readonly IReadOnlyList<TypeEtape> Types =
    [
        new(Http, "Appel HTTP", "Appel d'un service via un gabarit YamlHttpClient.",
            ["requete"], ["donnees"], true),
        new(AttendreEvenement, "Attente d'événement", "Attend un événement externe, avec délai optionnel.",
            ["evenement"], ["delai", "siDelaiExpire"], false),
        new(Courriel, "Courriel", "Envoi d'un courriel à partir d'un gabarit.",
            ["gabarit"], ["a", "cc", "cci", "donnees"], true),
        new(Delai, "Délai", "Pause durable pendant une durée ou jusqu'à une date.",
            [], ["duree", "jusqua"], false),
        new(Decision, "Décision", "Aiguillage selon les conditions de « suivant ».",
            [], [], false),
        new(Definir, "Variables", "Calcule des valeurs et les conserve dans « variables ».",
            ["variables"], [], false),
        new(SousProcessus, "Sous-processus", "Démarre un autre processus déployé et attend sa fin.",
            ["processus"], ["version", "entrees"], true),
        new(Reponse, "Réponse à l'appelant", "Publie la réponse HTTP d'un démarrage synchrone (?attendre=) ; le processus continue.",
            [], ["statutHttp", "corps"], false)
    ];

    public static TypeEtape? Trouver(string type) =>
        Types.FirstOrDefault(t => string.Equals(t.Nom, type, StringComparison.Ordinal));

    public static readonly IReadOnlyList<string> TypesEntree = ["string", "int", "number", "bool", "date", "object", "array"];
}
