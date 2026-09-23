namespace OIM.Moteur.Hebergement;

/// <summary>Section « Oim » de la configuration.</summary>
public sealed class OptionsOim
{
    public const string Section = "Oim";

    /// <summary>Chaîne de connexion SQL Server (sinon ConnectionStrings:Oim).</summary>
    public string? ConnexionSql { get; set; }

    /// <summary>Nom du task hub DurableTask : isole plusieurs applications dans la même base.</summary>
    public string TaskHub { get; set; } = "oim";

    /// <summary>Schéma SQL des tables DurableTask (défaut « dt »).</summary>
    public string SchemaDurableTask { get; set; } = "dt";

    /// <summary>
    /// Mode de task hub « application » : le task hub est déterminé par le nom d'application
    /// plutôt que par l'utilisateur SQL. Indispensable si plusieurs identités (IIS, développeurs,
    /// outils) doivent voir les mêmes instances.
    /// </summary>
    public bool TaskHubParApplication { get; set; } = true;

    public bool CreerBaseSiAbsente { get; set; } = true;

    /// <summary>Dossier de définitions déployées automatiquement au démarrage (un sous-dossier par processus).</summary>
    public string? DossierDefinitions { get; set; }

    /// <summary>Attente maximale d'un démarrage synchrone (?attendre=).</summary>
    public TimeSpan AttenteSynchroneMax { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Intervalle maximal de scrutation des orchestrations par le worker (défaut DurableTask : 3 s).
    /// Plus court = démarrages synchrones plus réactifs après une période d'inactivité.
    /// </summary>
    public TimeSpan ScrutationMax { get; set; } = TimeSpan.FromMilliseconds(500);

    public int MaxActivitesConcurrentes { get; set; } = Environment.ProcessorCount * 4;
    public int MaxOrchestrationsActives { get; set; } = Environment.ProcessorCount * 8;

    public OptionsCourriel Courriel { get; set; } = new();

    public OptionsTests Tests { get; set; } = new();
}

public enum ModeTestsDeploiement { Bloquant, Avertissement, Desactive }

public sealed class OptionsTests
{
    /// <summary>
    /// Tests métier (tests/*.yml du paquet) exécutés à chaque déploiement par l'API ou l'interface :
    /// Bloquant (défaut) = un cas en échec refuse la version; Avertissement = déploie et rapporte.
    /// Non exécutés pour le déploiement du dossier au démarrage (le moteur n'est pas encore démarré).
    /// </summary>
    public ModeTestsDeploiement AuDeploiement { get; set; } = ModeTestsDeploiement.Bloquant;
}

public sealed class OptionsCourriel
{
    /// <summary>Expéditeur par défaut si le gabarit n'en précise pas.</summary>
    public string De { get; set; } = "ne-pas-repondre@oim.local";

    public string? Hote { get; set; }
    public int Port { get; set; } = 25;
    public bool Ssl { get; set; }
    public string? Utilisateur { get; set; }
    public string? MotDePasse { get; set; }

    /// <summary>
    /// Si défini, les courriels sont écrits en fichiers .eml dans ce dossier au lieu d'être
    /// transmis (développement, tests).
    /// </summary>
    public string? DossierDepot { get; set; }

    /// <summary>Si défini, tous les courriels sont redirigés vers cette adresse (environnements de test).</summary>
    public string? RedirigerVers { get; set; }
}
