using Dapper;

namespace Store.Infrastructure;

public sealed class DatabaseInitializer(SqliteConnectionFactory connectionFactory)
{
    private const long CurrentVersion = 1;

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(connectionFactory.DatabasePath)!);
        using var connection = connectionFactory.OpenConnection();
        var journalMode = connection.QuerySingle<string>("PRAGMA journal_mode = WAL;");
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("No se pudo activar el modo WAL de SQLite.");
        }

        // BEGIN IMMEDIATE serializes schema changes across simultaneous local starts.
        using var transaction = connection.BeginTransaction();
        var version = connection.QuerySingle<long>("PRAGMA user_version;", transaction: transaction);
        if (version > CurrentVersion)
        {
            throw new InvalidOperationException("La base de datos pertenece a una versión posterior de la aplicación.");
        }

        if (version < 1)
        {
            using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream(
                "Store.Infrastructure.Migrations.001_initial.sql")
                ?? throw new InvalidOperationException("No se encontró la migración inicial.");
            using var reader = new StreamReader(stream);
            connection.Execute(reader.ReadToEnd(), transaction: transaction);
            connection.Execute("PRAGMA user_version = 1;", transaction: transaction);
        }

        transaction.Commit();
    }
}
