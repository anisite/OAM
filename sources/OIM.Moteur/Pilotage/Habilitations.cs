namespace OIM.Moteur.Pilotage;

/// <summary>
/// Droits de l'appelant, calculés à partir des sujets de son jeton (quel que soit l'émetteur) :
/// administrateur, support OIM, et équipes dont il est membre. Toutes les règles d'accès sont ici.
///
/// <list type="table">
///   <item>Lire, piloter : admin et support (toutes les équipes), membres (leurs équipes).</item>
///   <item>Déployer, activer, tests métier : admin (toutes), membres (leurs équipes).</item>
///   <item>Administrer les équipes : admin.</item>
/// </list>
/// Une ressource d'une équipe inaccessible est « introuvable » (404) : on ne révèle pas son existence.
/// </summary>
public sealed record Habilitations(string Utilisateur, bool Admin, bool Support, IReadOnlySet<string> Equipes)
{
    /// <summary>Opérations internes du moteur (déploiement du dossier au démarrage, tests).</summary>
    public static readonly Habilitations Systeme = new("systeme", true, false, new HashSet<string>());

    public bool VoitTout => Admin || Support;

    /// <summary>Accès à au moins une équipe (sinon l'API refuse l'appelant).</summary>
    public bool Autorise => VoitTout || Equipes.Count > 0;

    /// <summary>Équipes visibles, ou null pour toutes.</summary>
    public IReadOnlySet<string>? FiltreEquipes => VoitTout ? null : Equipes;

    public bool PeutLire(string? equipe) => equipe is not null && (VoitTout || Equipes.Contains(equipe));

    public bool PeutPiloter(string? equipe) => PeutLire(equipe);

    public bool PeutDeployer(string? equipe) => equipe is not null && (Admin || Equipes.Contains(equipe));

    public void ExigerLecture(string? equipe, string introuvable)
    {
        if (!PeutLire(equipe)) throw ErreurPilotage.Introuvable(introuvable);
    }

    public void ExigerPilotage(string? equipe, string introuvable)
    {
        if (!PeutPiloter(equipe)) throw ErreurPilotage.Introuvable(introuvable);
    }

    public void ExigerDeploiement(string? equipe, string introuvable)
    {
        ExigerLecture(equipe, introuvable);
        if (!PeutDeployer(equipe))
            throw ErreurPilotage.Interdit($"Seuls les membres de l'équipe « {equipe} » peuvent déployer ou tester ses processus.");
    }

    public void ExigerAdmin()
    {
        if (!Admin) throw ErreurPilotage.Interdit("Action réservée aux administrateurs OIM.");
    }
}
