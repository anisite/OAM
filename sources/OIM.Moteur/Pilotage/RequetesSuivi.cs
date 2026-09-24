using System.Text;
using Dapper;
using Microsoft.Extensions.Options;
using OIM.Moteur.Hebergement;
using OIM.Moteur.Stockage;

namespace OIM.Moteur.Pilotage;

public sealed record InstanceResume(
    string InstanceId,
    string Processus,
    string? Version,
    string Statut,
    DateTime Creee,
    DateTime MiseAJour,
    DateTime? Terminee,
    string? StatutMetier,
    string? Etape,
    string? EvenementAttendu,
    DateTime? Echeance,
    string? Erreur,
    string? ParentInstanceId);

public sealed record PageInstances(IReadOnlyList<InstanceResume> Elements, int Total, int Page, int Taille);

public sealed record FiltreInstances(
    string? Equipe = null,
    IReadOnlyList<string>? Statuts = null,
    string? Processus = null,
    string? Recherche = null,
    DateTime? Depuis = null,
    DateTime? Jusqua = null,
    bool EnAttenteEvenement = false,
    bool InclureSousProcessus = true,
    int Page = 1,
    int Taille = 25);

public sealed record StatistiqueProcessus(string Processus, string Statut, int Nombre);

public sealed record Statistiques(
    IReadOnlyDictionary<string, int> ParStatut,
    IReadOnlyList<StatistiqueProcessus> ParProcessus,
    int Demarrees24h,
    int Terminees24h,
    int Echouees24h,
    int EnAttenteEvenement,
    IReadOnlyList<InstanceResume> EchecsRecents,
    IReadOnlyList<InstanceResume> AttentesProches);

public sealed record EvenementHistorique(
    string ExecutionId,
    long Sequence,
    string Type,
    string? Nom,
    int? TacheId,
    DateTime Horodatage,
    string? Statut,
    string? Donnees);

/// <summary>
/// Requêtes de suivi directement sur les vues DurableTask (dt.vInstances, dt.vHistory) :
/// filtrage par processus, recherche plein texte et agrégats que l'API DurableTask n'offre pas.
/// Les équipes se filtrent sur le préfixe de l'InstanceID (« equipe.… », index de la clé primaire).
/// </summary>
public sealed class RequetesSuivi(ConnexionSql connexion, IOptions<OptionsOim> options)
{
    private string Dt => $"[{options.Value.SchemaDurableTask}]";

    private string SelectInstances => $"""
        SELECT i.InstanceID AS InstanceId, i.Name AS Processus, i.Version, i.RuntimeStatus AS Statut,
               i.CreatedTime AS Creee, i.LastUpdatedTime AS MiseAJour,
               CASE WHEN i.RuntimeStatus IN ('Completed','Failed','Terminated','Canceled') THEN i.CompletedTime END AS Terminee,
               s.StatutMetier, s.Etape, s.EvenementAttendu, TRY_CONVERT(datetime2, s.Echeance, 127) AS Echeance, s.Erreur,
               i.ParentInstanceID AS ParentInstanceId
        FROM {Dt}.vInstances i
        OUTER APPLY (SELECT
            CASE WHEN ISJSON(i.CustomStatusText) = 1 THEN JSON_VALUE(i.CustomStatusText, '$.statut') END AS StatutMetier,
            CASE WHEN ISJSON(i.CustomStatusText) = 1 THEN JSON_VALUE(i.CustomStatusText, '$.etape') END AS Etape,
            CASE WHEN ISJSON(i.CustomStatusText) = 1 THEN JSON_VALUE(i.CustomStatusText, '$.attente.evenement') END AS EvenementAttendu,
            CASE WHEN ISJSON(i.CustomStatusText) = 1 THEN JSON_VALUE(i.CustomStatusText, '$.attente.echeance') END AS Echeance,
            CASE WHEN ISJSON(i.CustomStatusText) = 1 THEN JSON_VALUE(i.CustomStatusText, '$.erreur') END AS Erreur) s
        """;

    public async Task<PageInstances> ListerAsync(Habilitations h, FiltreInstances f, CancellationToken ct = default)
    {
        var taille = Math.Clamp(f.Taille, 1, 200);
        var page = Math.Max(1, f.Page);
        var p = new DynamicParameters();
        if (ClauseEquipes(h, f.Equipe, p) is not { } equipes) return new PageInstances([], 0, page, taille);

        // Les instances de tests métier (brouillons « ~… ») ne font pas partie du suivi.
        var where = new StringBuilder("WHERE i.Name NOT LIKE '~%'").Append(equipes);

        if (f.Statuts is { Count: > 0 })
        {
            where.Append(" AND i.RuntimeStatus IN @statuts");
            p.Add("statuts", f.Statuts);
        }
        if (!string.IsNullOrWhiteSpace(f.Processus))
        {
            where.Append(" AND i.Name = @processus");
            p.Add("processus", f.Processus);
        }
        if (!string.IsNullOrWhiteSpace(f.Recherche))
        {
            // Recherche par identifiant, statut métier ou valeur d'entrée (ex. numéro de dossier).
            where.Append(" AND (i.InstanceID LIKE @recherche OR i.CustomStatusText LIKE @recherche OR i.InputText LIKE @recherche)");
            p.Add("recherche", $"%{f.Recherche.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")}%");
        }
        if (f.Depuis is { } depuis)
        {
            where.Append(" AND i.CreatedTime >= @depuis");
            p.Add("depuis", depuis.ToUniversalTime());
        }
        if (f.Jusqua is { } jusqua)
        {
            where.Append(" AND i.CreatedTime < @jusqua");
            p.Add("jusqua", jusqua.ToUniversalTime());
        }
        if (f.EnAttenteEvenement)
            where.Append(" AND i.RuntimeStatus = 'Running' AND s.EvenementAttendu IS NOT NULL");
        if (!f.InclureSousProcessus)
            where.Append(" AND i.ParentInstanceID IS NULL");

        p.Add("saut", (page - 1) * taille);
        p.Add("taille", taille);

        await using var cn = await connexion.OuvrirAsync(ct);
        using var multi = await cn.QueryMultipleAsync(new CommandDefinition($"""
            {SelectInstances}
            {where}
            ORDER BY i.CreatedTime DESC
            OFFSET @saut ROWS FETCH NEXT @taille ROWS ONLY;

            SELECT COUNT(*) FROM {Dt}.vInstances i
            OUTER APPLY (SELECT CASE WHEN ISJSON(i.CustomStatusText) = 1 THEN JSON_VALUE(i.CustomStatusText, '$.attente.evenement') END AS EvenementAttendu) s
            {where};
            """, p, cancellationToken: ct));

        var elements = (await multi.ReadAsync<InstanceResume>()).Select(Normaliser).ToList();
        var total = await multi.ReadSingleAsync<int>();
        return new PageInstances(elements, total, page, taille);
    }

    public async Task<Statistiques> StatistiquesAsync(Habilitations h, string? equipe = null, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        if (ClauseEquipes(h, equipe, p) is not { } equipes)
            return new Statistiques(new Dictionary<string, int>(), [], 0, 0, 0, 0, [], []);

        await using var cn = await connexion.OuvrirAsync(ct);
        using var multi = await cn.QueryMultipleAsync(new CommandDefinition($"""
            SELECT i.Name AS Processus, i.RuntimeStatus AS Statut, COUNT(*) AS Nombre
            FROM {Dt}.vInstances i WHERE i.Name NOT LIKE '~%'{equipes} GROUP BY i.Name, i.RuntimeStatus;

            SELECT
              SUM(CASE WHEN i.CreatedTime >= DATEADD(hour, -24, SYSUTCDATETIME()) THEN 1 ELSE 0 END),
              SUM(CASE WHEN i.RuntimeStatus = 'Completed' AND i.CompletedTime >= DATEADD(hour, -24, SYSUTCDATETIME()) THEN 1 ELSE 0 END),
              SUM(CASE WHEN i.RuntimeStatus = 'Failed' AND i.CompletedTime >= DATEADD(hour, -24, SYSUTCDATETIME()) THEN 1 ELSE 0 END)
            FROM {Dt}.vInstances i WHERE i.Name NOT LIKE '~%'{equipes};

            {SelectInstances}
            WHERE i.RuntimeStatus = 'Failed' AND i.Name NOT LIKE '~%'{equipes}
            ORDER BY i.LastUpdatedTime DESC OFFSET 0 ROWS FETCH NEXT 8 ROWS ONLY;

            {SelectInstances}
            WHERE i.RuntimeStatus = 'Running' AND s.EvenementAttendu IS NOT NULL AND i.Name NOT LIKE '~%'{equipes}
            ORDER BY CASE WHEN s.Echeance IS NULL THEN 1 ELSE 0 END, TRY_CONVERT(datetime2, s.Echeance, 127)
            OFFSET 0 ROWS FETCH NEXT 8 ROWS ONLY;

            SELECT COUNT(*) FROM {Dt}.vInstances i
            WHERE i.RuntimeStatus = 'Running' AND i.Name NOT LIKE '~%'{equipes} AND ISJSON(i.CustomStatusText) = 1
              AND JSON_VALUE(i.CustomStatusText, '$.attente.evenement') IS NOT NULL;
            """, p, cancellationToken: ct));

        var parProcessus = (await multi.ReadAsync<StatistiqueProcessus>()).ToList();
        var (demarrees, terminees, echouees) = await multi.ReadSingleAsync<(int?, int?, int?)>();
        var echecs = (await multi.ReadAsync<InstanceResume>()).Select(Normaliser).ToList();
        var attentes = (await multi.ReadAsync<InstanceResume>()).Select(Normaliser).ToList();
        var enAttente = await multi.ReadSingleAsync<int>();

        var parStatut = parProcessus.GroupBy(x => x.Statut).ToDictionary(g => g.Key, g => g.Sum(x => x.Nombre));
        return new Statistiques(parStatut, parProcessus, demarrees ?? 0, terminees ?? 0, echouees ?? 0, enAttente, echecs, attentes);
    }

    /// <param name="complet">false : données tronquées à 8000 caractères (affichage).</param>
    public async Task<IReadOnlyList<EvenementHistorique>> HistoriqueAsync(Habilitations h, string instanceId, CancellationToken ct = default,
        bool complet = false)
    {
        h.ExigerLecture(IdsEquipe.Equipe(instanceId), $"Instance « {instanceId} » introuvable.");
        await using var cn = await connexion.OuvrirAsync(ct);
        var lignes = await cn.QueryAsync<EvenementHistorique>(new CommandDefinition($"""
            SELECT ExecutionID AS ExecutionId, SequenceNumber AS Sequence, EventType AS Type, Name AS Nom, TaskID AS TacheId,
                   Timestamp AS Horodatage, RuntimeStatus AS Statut, {(complet ? "Payload" : "LEFT(Payload, 8000)")} AS Donnees
            FROM {Dt}.vHistory
            WHERE InstanceID = @instanceId
            ORDER BY Timestamp, SequenceNumber
            """, new { instanceId }, cancellationToken: ct));
        return lignes.Select(l => l with { Horodatage = DateTime.SpecifyKind(l.Horodatage, DateTimeKind.Utc) }).ToList();
    }

    /// <summary>Instances de premier niveau actives, en échec et en attente d'un événement, par équipe.</summary>
    public async Task<IReadOnlyDictionary<string, CompteursEquipe>> CompteursParEquipeAsync(IReadOnlyCollection<string> equipes,
        CancellationToken ct = default)
    {
        if (equipes.Count == 0) return new Dictionary<string, CompteursEquipe>();
        await using var cn = await connexion.OuvrirAsync(ct);
        var lignes = await cn.QueryAsync<(string Equipe, int Actives, int Echecs, int EnAttente)>(new CommandDefinition($"""
            SELECT LEFT(i.InstanceID, CHARINDEX('.', i.InstanceID) - 1) AS Equipe,
                   SUM(CASE WHEN i.RuntimeStatus IN ('Running','Pending','Suspended') THEN 1 ELSE 0 END),
                   SUM(CASE WHEN i.RuntimeStatus = 'Failed' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN i.RuntimeStatus = 'Running' AND ISJSON(i.CustomStatusText) = 1
                             AND JSON_VALUE(i.CustomStatusText, '$.attente.evenement') IS NOT NULL THEN 1 ELSE 0 END)
            FROM {Dt}.vInstances i
            WHERE i.RuntimeStatus IN ('Running','Pending','Suspended','Failed') AND i.Name NOT LIKE '~%'
              AND i.ParentInstanceID IS NULL AND CHARINDEX('.', i.InstanceID) > 1
            GROUP BY LEFT(i.InstanceID, CHARINDEX('.', i.InstanceID) - 1)
            """, cancellationToken: ct));
        var voulues = equipes.ToHashSet(StringComparer.Ordinal);
        return lignes.Where(l => voulues.Contains(l.Equipe))
            .ToDictionary(l => l.Equipe, l => new CompteursEquipe(l.Actives, l.Echecs, l.EnAttente));
    }

    /// <summary>
    /// Restriction aux équipes visibles (une seule si <paramref name="equipe"/> est précisée) :
    /// chaîne vide si aucune restriction, null si aucune équipe n'est visible (résultat vide).
    /// </summary>
    private static string? ClauseEquipes(Habilitations h, string? equipe, DynamicParameters p)
    {
        IReadOnlyCollection<string>? equipes;
        if (!string.IsNullOrEmpty(equipe))
        {
            h.ExigerLecture(equipe, $"Équipe « {equipe} » introuvable.");
            equipes = [equipe];
        }
        else equipes = h.FiltreEquipes;

        if (equipes is null) return "";
        if (equipes.Count == 0) return null;
        var conditions = equipes.Select((e, n) =>
        {
            p.Add($"equipe{n}", $"{e}{IdsEquipe.Separateur}%");
            return $"i.InstanceID LIKE @equipe{n}";
        }).ToList();
        return $" AND ({string.Join(" OR ", conditions)})";
    }

    private static InstanceResume Normaliser(InstanceResume r) => r with
    {
        Creee = DateTime.SpecifyKind(r.Creee, DateTimeKind.Utc),
        MiseAJour = DateTime.SpecifyKind(r.MiseAJour, DateTimeKind.Utc),
        Terminee = r.Terminee is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null,
        Echeance = r.Echeance is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null
    };
}
