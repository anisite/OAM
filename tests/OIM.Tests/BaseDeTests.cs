using Microsoft.Data.SqlClient;

namespace OIM.Tests;

/// <summary>
/// Base d'intégration partagée (LocalDB « OIM », OIM_Tests). Les classes de tests démarrent en parallèle :
/// la base est créée une seule fois ici, sinon deux CREATE DATABASE simultanés de DurableTask échouent.
/// </summary>
internal static class BaseDeTests
{
    public const string Connexion = @"Server=(localdb)\OIM;Database=OIM_Tests;Integrated Security=True;TrustServerCertificate=True";

    private static readonly SemaphoreSlim Verrou = new(1, 1);

    /// <returns>false si l'instance LocalDB est indisponible (tests « non concluants »).</returns>
    public static async Task<bool> AssurerAsync()
    {
        await Verrou.WaitAsync();
        try
        {
            await using var cn = new SqlConnection(Connexion.Replace("Database=OIM_Tests", "Database=master"));
            await cn.OpenAsync();
            await using var commande = new SqlCommand(
                "IF DB_ID('OIM_Tests') IS NULL CREATE DATABASE OIM_Tests COLLATE Latin1_General_100_BIN2_UTF8", cn);
            await commande.ExecuteNonQueryAsync();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
        finally
        {
            Verrou.Release();
        }
    }
}
