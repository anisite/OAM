using Dapper;

namespace OIM.Moteur.Stockage;

public sealed record Equipe(string Id, string Nom, string? Description, bool Actif, DateTime CreeLe);

/// <summary>Membre d'une équipe active : sujet de jeton → équipe.</summary>
public sealed record Adhesion(string Sujet, string EquipeId);

/// <summary>Équipes (schéma oim) et leurs membres.</summary>
public sealed class DepotEquipesSql(ConnexionSql connexion)
{
    public async Task<IReadOnlyList<Equipe>> ListerAsync(CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        var lignes = await cn.QueryAsync<Equipe>(new CommandDefinition(
            "SELECT Id, Nom, Description, Actif, CreeLe FROM oim.Equipes ORDER BY Nom, Id", cancellationToken: ct));
        return lignes.Select(Utc).ToList();
    }

    public async Task<Equipe?> ObtenirAsync(string id, CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        var e = await cn.QuerySingleOrDefaultAsync<Equipe>(new CommandDefinition(
            "SELECT Id, Nom, Description, Actif, CreeLe FROM oim.Equipes WHERE Id = @id", new { id }, cancellationToken: ct));
        return e is null ? null : Utc(e);
    }

    public async Task<IReadOnlyList<string>> MembresAsync(string id, CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        return (await cn.QueryAsync<string>(new CommandDefinition(
            "SELECT Sujet FROM oim.EquipeMembres WHERE EquipeId = @id ORDER BY Sujet", new { id }, cancellationToken: ct))).ToList();
    }

    /// <summary>Membres de toutes les équipes actives (cache des habilitations).</summary>
    public async Task<IReadOnlyList<Adhesion>> AdhesionsAsync(CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        return (await cn.QueryAsync<Adhesion>(new CommandDefinition("""
            SELECT m.Sujet, m.EquipeId FROM oim.EquipeMembres m
            JOIN oim.Equipes e ON e.Id = m.EquipeId
            WHERE e.Actif = 1
            """, cancellationToken: ct))).ToList();
    }

    /// <returns>false si l'équipe existe déjà.</returns>
    public async Task<bool> CreerAsync(string id, string nom, string? description, CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        return await cn.ExecuteAsync(new CommandDefinition("""
            INSERT oim.Equipes (Id, Nom, Description)
            SELECT @id, @nom, @description WHERE NOT EXISTS (SELECT 1 FROM oim.Equipes WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id)
            """, new { id, nom, description }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> ModifierAsync(string id, string nom, string? description, bool actif, CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        return await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE oim.Equipes SET Nom = @nom, Description = @description, Actif = @actif WHERE Id = @id",
            new { id, nom, description, actif }, cancellationToken: ct)) > 0;
    }

    public async Task<int> NombreDefinitionsAsync(string id, CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM oim.Definitions WHERE Equipe = @id", new { id }, cancellationToken: ct));
    }

    public async Task<bool> SupprimerAsync(string id, CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        return await cn.ExecuteAsync(new CommandDefinition("DELETE oim.Equipes WHERE Id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    /// <summary>Remplace la liste des membres.</summary>
    public async Task DefinirMembresAsync(string id, IReadOnlyCollection<string> sujets, CancellationToken ct = default)
    {
        await using var cn = await connexion.OuvrirAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await cn.ExecuteAsync(new CommandDefinition("DELETE oim.EquipeMembres WHERE EquipeId = @id", new { id }, tx, cancellationToken: ct));
        if (sujets.Count > 0)
            await cn.ExecuteAsync(new CommandDefinition(
                "INSERT oim.EquipeMembres (EquipeId, Sujet) VALUES (@id, @sujet)",
                sujets.Select(sujet => new { id, sujet }), tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    private static Equipe Utc(Equipe e) => e with { CreeLe = DateTime.SpecifyKind(e.CreeLe, DateTimeKind.Utc) };
}
