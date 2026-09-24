using OIM.Moteur.Stockage;

namespace OIM.Moteur.Pilotage;

public sealed record CompteursEquipe(int Actives, int Echecs, int EnAttente);

public sealed record ResumeEquipe(string Id, string Nom, string? Description, bool Actif, CompteursEquipe Compteurs);

public sealed record EquipeDetail(string Id, string Nom, string? Description, bool Actif, DateTime CreeLe, IReadOnlyList<string> Membres);

/// <summary>
/// Équipes et appartenance. Les membres sont des sujets de jeton; <see cref="EquipesDesSujetsAsync"/>
/// sert à chaque requête et s'appuie sur un cache rafraîchi toutes les minutes (vidé localement à
/// chaque modification; les autres serveurs suivent au plus une minute plus tard).
/// </summary>
public sealed class ServiceEquipes(DepotEquipesSql depot, RequetesSuivi suivi)
{
    private static readonly TimeSpan DureeCache = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _verrou = new(1, 1);
    private volatile Cache? _cache;

    private sealed record Cache(IReadOnlyDictionary<string, string[]> ParSujet, DateTime Expiration);

    // ── Habilitations ────────────────────────────────────────────────────────

    /// <summary>Équipes actives dont au moins un sujet est membre (comparaison insensible à la casse).</summary>
    public async Task<IReadOnlySet<string>> EquipesDesSujetsAsync(IEnumerable<string> sujets, CancellationToken ct = default)
    {
        var cache = await CacheAsync(ct);
        var equipes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sujet in sujets)
            if (cache.ParSujet.TryGetValue(sujet, out var ids)) equipes.UnionWith(ids);
        return equipes;
    }

    /// <summary>Tous les sujets membres d'une équipe active (limite les groupes écrits dans un jeton).</summary>
    public async Task<IReadOnlyCollection<string>> SujetsConnusAsync(CancellationToken ct = default) =>
        (await CacheAsync(ct)).ParSujet.Keys.ToList();

    private async Task<Cache> CacheAsync(CancellationToken ct)
    {
        if (_cache is { } c && c.Expiration > DateTime.UtcNow) return c;
        await _verrou.WaitAsync(ct);
        try
        {
            if (_cache is { } c2 && c2.Expiration > DateTime.UtcNow) return c2;
            var parSujet = (await depot.AdhesionsAsync(ct))
                .GroupBy(a => a.Sujet, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(a => a.EquipeId).Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
            return _cache = new Cache(parSujet, DateTime.UtcNow + DureeCache);
        }
        finally
        {
            _verrou.Release();
        }
    }

    private void Invalider() => _cache = null;

    // ── Consultation ─────────────────────────────────────────────────────────

    /// <summary>Équipes visibles (les siennes; toutes pour admin et support) avec leurs compteurs d'instances.</summary>
    public async Task<IReadOnlyList<ResumeEquipe>> ListerAsync(Habilitations h, CancellationToken ct = default)
    {
        var equipes = (await depot.ListerAsync(ct))
            .Where(e => h.VoitTout || (e.Actif && h.Equipes.Contains(e.Id)))
            .ToList();
        var compteurs = await suivi.CompteursParEquipeAsync(equipes.Select(e => e.Id).ToList(), ct);
        return equipes
            .Select(e => new ResumeEquipe(e.Id, e.Nom, e.Description, e.Actif, compteurs.GetValueOrDefault(e.Id) ?? new CompteursEquipe(0, 0, 0)))
            .ToList();
    }

    public async Task<EquipeDetail> ObtenirAsync(Habilitations h, string id, CancellationToken ct = default)
    {
        h.ExigerLecture(id, Introuvable(id));
        var e = await depot.ObtenirAsync(id, ct) ?? throw ErreurPilotage.Introuvable(Introuvable(id));
        // La liste des membres (groupes, comptes) n'est montrée qu'aux administrateurs.
        var membres = h.Admin ? await depot.MembresAsync(id, ct) : [];
        return new EquipeDetail(e.Id, e.Nom, e.Description, e.Actif, e.CreeLe, membres);
    }

    /// <summary>Équipe existante et active (déploiement, démarrage).</summary>
    public async Task ExigerActiveAsync(string id, CancellationToken ct = default)
    {
        var e = await depot.ObtenirAsync(id, ct) ?? throw ErreurPilotage.Introuvable(Introuvable(id));
        if (!e.Actif) throw ErreurPilotage.Conflit($"L'équipe « {id} » est désactivée.");
    }

    // ── Administration ───────────────────────────────────────────────────────

    public async Task CreerAsync(Habilitations h, string id, string nom, string? description, IReadOnlyCollection<string>? membres,
        CancellationToken ct = default)
    {
        h.ExigerAdmin();
        if (!IdsEquipe.EstIdEquipeValide(id))
            throw ErreurPilotage.Invalide($"Identifiant d'équipe « {id} » invalide : minuscules, chiffres et tirets (50 au plus), sans point; « admin », « api »… sont réservés.");
        if (string.IsNullOrWhiteSpace(nom)) throw ErreurPilotage.Invalide("Le nom de l'équipe est requis.");
        if (!await depot.CreerAsync(id, nom.Trim(), Nettoyer(description), ct))
            throw ErreurPilotage.Conflit($"L'équipe « {id} » existe déjà.");
        if (membres is { Count: > 0 }) await depot.DefinirMembresAsync(id, NormaliserSujets(membres), ct);
        Invalider();
    }

    public async Task ModifierAsync(Habilitations h, string id, string nom, string? description, bool actif, CancellationToken ct = default)
    {
        h.ExigerAdmin();
        if (string.IsNullOrWhiteSpace(nom)) throw ErreurPilotage.Invalide("Le nom de l'équipe est requis.");
        if (!await depot.ModifierAsync(id, nom.Trim(), Nettoyer(description), actif, ct))
            throw ErreurPilotage.Introuvable(Introuvable(id));
        Invalider();
    }

    public async Task DefinirMembresAsync(Habilitations h, string id, IReadOnlyCollection<string> sujets, CancellationToken ct = default)
    {
        h.ExigerAdmin();
        _ = await depot.ObtenirAsync(id, ct) ?? throw ErreurPilotage.Introuvable(Introuvable(id));
        await depot.DefinirMembresAsync(id, NormaliserSujets(sujets), ct);
        Invalider();
    }

    public async Task SupprimerAsync(Habilitations h, string id, CancellationToken ct = default)
    {
        h.ExigerAdmin();
        if (await depot.NombreDefinitionsAsync(id, ct) is > 0 and var n)
            throw ErreurPilotage.Conflit($"L'équipe « {id} » a encore {n} processus : désactivez-la plutôt.");
        if (!await depot.SupprimerAsync(id, ct))
            throw ErreurPilotage.Introuvable(Introuvable(id));
        Invalider();
    }

    /// <summary>Crée l'équipe si elle n'existe pas (déploiement du dossier de définitions au démarrage).</summary>
    public async Task AssurerAsync(string id, CancellationToken ct = default)
    {
        if (!IdsEquipe.EstIdEquipeValide(id))
            throw new InvalidDataException($"Identifiant d'équipe « {id} » invalide (minuscules, chiffres, tirets).");
        if (await depot.CreerAsync(id, id, null, ct)) Invalider();
    }

    private static List<string> NormaliserSujets(IEnumerable<string> sujets)
    {
        var liste = sujets.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (liste.FirstOrDefault(s => s.Length > 256) is { } trop)
            throw ErreurPilotage.Invalide($"Sujet trop long (256 caractères au plus) : {trop}");
        return liste;
    }

    private static string? Nettoyer(string? texte) => string.IsNullOrWhiteSpace(texte) ? null : texte.Trim();

    private static string Introuvable(string id) => $"Équipe « {id} » introuvable.";
}
