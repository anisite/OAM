using System.Collections.Concurrent;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using OIM.Moteur.Definitions;
using OIM.Moteur.Pilotage;

namespace OIM.Moteur.Stockage;

public sealed class DepotDefinitionsSql(ConnexionSql connexion) : IDepotDefinitions
{
    // Une version déployée est immuable : on peut la garder en mémoire indéfiniment.
    private readonly ConcurrentDictionary<(string, int), VersionDefinition> _cache = new();

    public async Task InitialiserAsync(CancellationToken ct = default)
    {
        await using var flux = typeof(DepotDefinitionsSql).Assembly
            .GetManifestResourceStream("OIM.Moteur.Stockage.SchemaOim.sql")!;
        using var lecteur = new StreamReader(flux);
        var script = await lecteur.ReadToEndAsync(ct);

        // Plusieurs serveurs peuvent démarrer en même temps : un seul crée le schéma.
        await using var cn = await connexion.OuvrirAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await VerrouAsync(cn, tx, "oim:schema", ct);
        await cn.ExecuteAsync(new CommandDefinition(script, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Verrou applicatif SQL Server lié à la transaction : sérialise une opération entre tous les
    /// serveurs qui partagent la base (ex. deux nœuds IIS qui déploient au démarrage).
    /// </summary>
    private static async Task VerrouAsync(SqlConnection cn, SqlTransaction tx, string ressource, CancellationToken ct)
    {
        var p = new DynamicParameters(new { Resource = ressource, LockMode = "Exclusive", LockOwner = "Transaction", LockTimeout = 60000 });
        p.Add("retour", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);
        await cn.ExecuteAsync(new CommandDefinition("sp_getapplock", p, tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        if (p.Get<int>("retour") < 0)
            throw new TimeoutException($"Verrou « {ressource} » non obtenu (code {p.Get<int>("retour")}).");
    }

    public async Task<ResultatDeploiement> DeployerAsync(string equipe, DefinitionProcessus definition, PaquetDefinition paquet,
        string? deployePar, string? commentaire, CancellationToken ct = default)
    {
        var empreinte = paquet.Empreinte();
        var id = IdsEquipe.Qualifier(equipe, definition.Id);
        if (id.Length > 160)
            throw new InvalidDataException($"Identifiant « {id} » trop long (160 caractères au plus, équipe comprise).");
        if (paquet.Fichiers.Keys.FirstOrDefault(c => c.Length > 360) is { } chemin)
            throw new InvalidDataException($"Chemin de fichier trop long (360 caractères au plus) : {chemin}");

        await using var cn = await connexion.OuvrirAsync(ct);
        // ReadCommitted + UPDLOCK/HOLDLOCK : ne jamais laisser un niveau Serializable sur une connexion
        // du pool, partagé avec DurableTask (READPAST exige READ COMMITTED).
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        // Deux déploiements simultanés du même processus (ex. deux serveurs au démarrage) :
        // le second attend le premier, puis constate que le contenu est identique.
        await VerrouAsync(cn, tx, $"oim:deploiement:{id}", ct);

        var courante = await cn.QuerySingleOrDefaultAsync<LigneCourante>(new CommandDefinition("""
            SELECT d.VersionCourante AS Version, v.Empreinte
            FROM oim.Definitions d WITH (UPDLOCK, HOLDLOCK)
            JOIN oim.DefinitionVersions v ON v.DefinitionId = d.Id AND v.Version = d.VersionCourante
            WHERE d.Id = @Id
            """, new { Id = id }, tx, cancellationToken: ct));

        if (courante is { } c && c.Empreinte.Trim() == empreinte)
        {
            // Contenu identique : on réactive simplement la définition au besoin.
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE oim.Definitions SET Actif = 1 WHERE Id = @Id AND Actif = 0", new { Id = id }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return new ResultatDeploiement(id, c.Version, false);
        }

        var version = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT ISNULL(MAX(Version), 0) + 1 FROM oim.DefinitionVersions WHERE DefinitionId = @Id",
            new { Id = id }, tx, cancellationToken: ct));

        if (courante is null)
            await cn.ExecuteAsync(new CommandDefinition("""
                IF NOT EXISTS (SELECT 1 FROM oim.Definitions WHERE Id = @Id)
                    INSERT oim.Definitions (Id, Equipe, Nom, Description, VersionCourante) VALUES (@Id, @Equipe, @Nom, @Description, 0)
                """, new { Id = id, Equipe = equipe, definition.Nom, definition.Description }, tx, cancellationToken: ct));

        await cn.ExecuteAsync(new CommandDefinition("""
            INSERT oim.DefinitionVersions (DefinitionId, Version, Yaml, Empreinte, DeployePar, Commentaire)
            VALUES (@Id, @Version, @Yaml, @Empreinte, @DeployePar, @Commentaire)
            """, new { Id = id, Version = version, paquet.Yaml, Empreinte = empreinte, DeployePar = deployePar, Commentaire = commentaire },
            tx, cancellationToken: ct));

        if (paquet.Fichiers.Count > 0)
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT oim.DefinitionFichiers (DefinitionId, Version, Chemin, Contenu) VALUES (@Id, @Version, @Chemin, @Contenu)
                """, paquet.Fichiers.Select(f => new { Id = id, Version = version, Chemin = f.Key, Contenu = f.Value }),
                tx, cancellationToken: ct));

        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE oim.Definitions
            SET VersionCourante = @Version, Nom = @Nom, Description = @Description, Actif = 1, ModifieLe = SYSUTCDATETIME()
            WHERE Id = @Id
            """, new { Id = id, Version = version, definition.Nom, definition.Description }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new ResultatDeploiement(id, version, true);
    }

    public async Task<VersionDefinition?> ObtenirAsync(string id, int? version = null, CancellationToken ct = default)
    {
        if (version is { } v && _cache.TryGetValue((id, v), out var enCache)) return enCache;
        if (id.StartsWith(IDepotDefinitions.PrefixeBrouillon, StringComparison.Ordinal))
            return await ObtenirBrouillonAsync(id, ct);

        await using var cn = await connexion.OuvrirAsync(ct);
        var ligne = await cn.QuerySingleOrDefaultAsync<LigneVersion>(new CommandDefinition("""
            SELECT v.DefinitionId, v.Version, v.Yaml, v.Empreinte, v.DeployePar, v.DeployeLe, v.Commentaire
            FROM oim.DefinitionVersions v
            JOIN oim.Definitions d ON d.Id = v.DefinitionId
            WHERE v.DefinitionId = @id AND v.Version = ISNULL(@version, d.VersionCourante)
            """, new { id, version }, cancellationToken: ct));
        if (ligne is null) return null;

        var fichiers = (await cn.QueryAsync<(string Chemin, string Contenu)>(new CommandDefinition(
                "SELECT Chemin, Contenu FROM oim.DefinitionFichiers WHERE DefinitionId = @id AND Version = @Version",
                new { id, ligne.Version }, cancellationToken: ct)))
            .ToDictionary(f => f.Chemin, f => f.Contenu, StringComparer.OrdinalIgnoreCase);

        var resultat = new VersionDefinition(ligne.DefinitionId, ligne.Version, ligne.Yaml, fichiers,
            ligne.Empreinte, ligne.DeployePar, DateTime.SpecifyKind(ligne.DeployeLe, DateTimeKind.Utc), ligne.Commentaire);
        _cache[(ligne.DefinitionId, ligne.Version)] = resultat;
        return resultat;
    }

    public async Task<IReadOnlyList<ResumeDefinition>> ListerAsync(IReadOnlyCollection<string>? equipes, CancellationToken ct = default)
    {
        if (equipes is { Count: 0 }) return [];
        await using var cn = await connexion.OuvrirAsync(ct);
        var lignes = await cn.QueryAsync<ResumeDefinition>(new CommandDefinition($"""
            SELECT d.Id, d.Equipe, d.Nom, d.Description, d.VersionCourante, d.Actif, d.ModifieLe,
                   v.DeployePar, (SELECT COUNT(*) FROM oim.DefinitionVersions x WHERE x.DefinitionId = d.Id) AS NbVersions
            FROM oim.Definitions d
            JOIN oim.DefinitionVersions v ON v.DefinitionId = d.Id AND v.Version = d.VersionCourante
            {(equipes is null ? "" : "WHERE d.Equipe IN @equipes")}
            ORDER BY d.Id
            """, new { equipes }, cancellationToken: ct));
        return lignes.Select(l => l with { ModifieLe = DateTime.SpecifyKind(l.ModifieLe, DateTimeKind.Utc) }).ToList();
    }

    public async Task<IReadOnlyList<ResumeVersion>> VersionsAsync(string id, CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        var lignes = await cn.QueryAsync<ResumeVersion>(new CommandDefinition("""
            SELECT Version, Empreinte, DeployePar, DeployeLe, Commentaire
            FROM oim.DefinitionVersions WHERE DefinitionId = @id ORDER BY Version DESC
            """, new { id }, cancellationToken: ct));
        return lignes.Select(l => l with { DeployeLe = DateTime.SpecifyKind(l.DeployeLe, DateTimeKind.Utc) }).ToList();
    }

    public async Task<bool> DefinirActifAsync(string id, bool actif, CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        return await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE oim.Definitions SET Actif = @actif, ModifieLe = SYSUTCDATETIME() WHERE Id = @id",
            new { id, actif }, cancellationToken: ct)) > 0;
    }

    public async Task EnregistrerBrouillonAsync(string id, PaquetDefinition paquet, CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("""
            DELETE TOP (100) oim.Brouillons WITH (READPAST) WHERE CreeLe < DATEADD(day, -1, SYSUTCDATETIME());
            INSERT oim.Brouillons (Id, Yaml, Fichiers) VALUES (@id, @Yaml, @fichiers);
            """, new { id, paquet.Yaml, fichiers = System.Text.Json.JsonSerializer.Serialize(paquet.Fichiers) }, cancellationToken: ct));
    }

    public async Task SupprimerBrouillonAsync(string id, CancellationToken ct = default)
    {
        _cache.TryRemove((id, 1), out _);
        await using var cn = await connexion.OuvrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("DELETE oim.Brouillons WHERE Id = @id", new { id }, cancellationToken: ct));
    }

    private async Task<VersionDefinition?> ObtenirBrouillonAsync(string id, CancellationToken ct)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        var ligne = await cn.QuerySingleOrDefaultAsync<LigneBrouillon>(new CommandDefinition(
            "SELECT Yaml, Fichiers, CreeLe FROM oim.Brouillons WHERE Id = @id", new { id }, cancellationToken: ct));
        if (ligne is not { } l) return null;

        var fichiers = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(l.Fichiers) ?? [];
        var resultat = new VersionDefinition(id, 1, l.Yaml, new Dictionary<string, string>(fichiers, StringComparer.OrdinalIgnoreCase),
            new PaquetDefinition(l.Yaml, fichiers).Empreinte(), "tests", DateTime.SpecifyKind(l.CreeLe, DateTimeKind.Utc), "Brouillon de test");
        _cache[(id, 1)] = resultat;
        return resultat;
    }

    private sealed record LigneBrouillon(string Yaml, string Fichiers, DateTime CreeLe);

    private sealed record LigneCourante(int Version, string Empreinte);

    private sealed record LigneVersion(string DefinitionId, int Version, string Yaml, string Empreinte,
        string? DeployePar, DateTime DeployeLe, string? Commentaire);
}
