using Dapper;

namespace Store.Infrastructure;

public sealed class DatabaseInitializer(SqliteConnectionFactory connectionFactory)
{
    private const long CurrentVersion = 2;

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
            connection.Execute(ReadMigration("001_initial.sql"), transaction: transaction);
            connection.Execute("PRAGMA user_version = 1;", transaction: transaction);
        }

        if (version < 2)
        {
            connection.Execute(ReadMigration("002_identity.sql"), transaction: transaction);
            connection.Execute("PRAGMA user_version = 2;", transaction: transaction);
        }

        transaction.Commit();
    }

    private static string ReadMigration(string name)
    {
        using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream(
            $"Store.Infrastructure.Migrations.{name}")
            ?? throw new InvalidOperationException($"No se encontró la migración {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
