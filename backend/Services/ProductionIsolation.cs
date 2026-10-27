using FactoryQuoteApi.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FactoryQuoteApi.Services;

public static class ProductionIsolation
{
    public static void ValidateConnection(string connectionString, bool production)
    {
        if (!production) return;
        SqlConnectionStringBuilder connection;
        try { connection = new(connectionString); }
        catch { throw new InvalidOperationException("Invalid production database configuration."); }
        var server = connection.DataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase);
        if (server != "db76143.public.databaseasp.net" && server != "db76143.public.databaseasp.net,1433")
            throw new InvalidOperationException("Unapproved ZIN STONE database server.");
        if (!string.Equals(connection.InitialCatalog, "db76143", StringComparison.Ordinal))
            throw new InvalidOperationException("Unapproved ZIN STONE database identity.");
        if (connection.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("Database encryption is required.");
    }

    public static async Task VerifyConnectedDatabaseAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT DB_NAME()";
            if (!string.Equals(await command.ExecuteScalarAsync() as string, "db76143", StringComparison.Ordinal))
                throw new InvalidOperationException("Connected database is not approved for ZIN STONE.");
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
