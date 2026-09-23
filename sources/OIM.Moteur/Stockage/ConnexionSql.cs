using Microsoft.Data.SqlClient;

namespace OIM.Moteur.Stockage;

/// <summary>
/// Chaîne de connexion partagée par DurableTask et OIM. On réutilise celle du fournisseur
/// DurableTask (qui fixe « Application Name » au nom du task hub) : les vues dt.* filtrent
/// alors sur le même task hub que le moteur.
/// </summary>
public sealed class ConnexionSql(string chaine)
{
    public string Chaine { get; } = chaine;

    public async Task<SqlConnection> OuvrirAsync(CancellationToken ct = default)
    {
        var cn = new SqlConnection(Chaine);
        await cn.OpenAsync(ct);
        return cn;
    }
}
