using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Store.Application;
using Store.Domain;

namespace Store.Infrastructure;

public sealed class SqliteStoreRepository(SqliteConnectionFactory connectionFactory) : IStoreRepository
{
    private const string GadgetColumns = "Id, Name, Description, PriceCents, CategoryId, Version, CreatedUtc";

    public IReadOnlyList<Category> GetCategories(int offset, int limit)
    {
        using var connection = connectionFactory.OpenConnection();
        return connection.Query<CategoryRow>(
            "SELECT Id, Name, CreatedUtc FROM Categories ORDER BY Id LIMIT @Limit OFFSET @Offset;",
            new { Limit = limit, Offset = offset }).Select(ToCategory).ToArray();
    }

    public Category? GetCategory(long id)
    {
        using var connection = connectionFactory.OpenConnection();
        var row = connection.QuerySingleOrDefault<CategoryRow>(
            "SELECT Id, Name, CreatedUtc FROM Categories WHERE Id = @Id;", new { Id = id });
        return row is null ? null : ToCategory(row);
    }

    public Category AddCategory(string name, DateTimeOffset createdUtc)
    {
        using var connection = connectionFactory.OpenConnection();
        try
        {
            var row = connection.QuerySingle<CategoryRow>(
                """
                INSERT INTO Categories (Name, CreatedUtc) VALUES (@Name, @CreatedUtc)
                RETURNING Id, Name, CreatedUtc;
                """,
                new { Name = name, CreatedUtc = FormatUtc(createdUtc) });
            return ToCategory(row);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            throw new ConflictException("Ya existe una categoría con ese nombre.");
        }
    }

    public IReadOnlyList<Gadget> GetGadgets(long? categoryId, int offset, int limit)
    {
        using var connection = connectionFactory.OpenConnection();
        var sql = categoryId.HasValue
            ? $"SELECT {GadgetColumns} FROM Gadgets WHERE CategoryId = @CategoryId ORDER BY Id LIMIT @Limit OFFSET @Offset;"
            : $"SELECT {GadgetColumns} FROM Gadgets ORDER BY Id LIMIT @Limit OFFSET @Offset;";
        return connection.Query<GadgetRow>(sql, new { CategoryId = categoryId, Limit = limit, Offset = offset })
            .Select(ToGadget).ToArray();
    }

    public Gadget? GetGadget(long id)
    {
        using var connection = connectionFactory.OpenConnection();
        var row = connection.QuerySingleOrDefault<GadgetRow>(
            $"SELECT {GadgetColumns} FROM Gadgets WHERE Id = @Id;", new { Id = id });
        return row is null ? null : ToGadget(row);
    }

    public Gadget AddGadget(GadgetDetails details, DateTimeOffset createdUtc)
    {
        using var connection = connectionFactory.OpenConnection();
        try
        {
            var row = connection.QuerySingle<GadgetRow>(
                $"""
                INSERT INTO Gadgets (Name, Description, PriceCents, CategoryId, CreatedUtc)
                VALUES (@Name, @Description, @PriceCents, @CategoryId, @CreatedUtc)
                RETURNING {GadgetColumns};
                """,
                new { details.Name, details.Description, details.PriceCents, details.CategoryId, CreatedUtc = FormatUtc(createdUtc) });
            return ToGadget(row);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 787)
        {
            throw new ConflictException("La categoría cambió durante la operación; vuelva a consultar los datos.");
        }
    }

    public Gadget UpdateGadget(long id, GadgetDetails details, long expectedVersion)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var row = connection.QuerySingleOrDefault<GadgetRow>(
                $"""
                UPDATE Gadgets
                SET Name = @Name, Description = @Description, PriceCents = @PriceCents,
                    CategoryId = @CategoryId, Version = Version + 1
                WHERE Id = @Id AND Version = @ExpectedVersion
                RETURNING {GadgetColumns};
                """,
                new { Id = id, details.Name, details.Description, details.PriceCents, details.CategoryId, ExpectedVersion = expectedVersion },
                transaction);
            if (row is null)
            {
                ThrowMissingOrConflict(connection, transaction, id);
            }

            transaction.Commit();
            return ToGadget(row!);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 787)
        {
            throw new ConflictException("La categoría cambió durante la operación; vuelva a consultar los datos.");
        }
    }

    public void DeleteGadget(long id, long expectedVersion)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var affected = connection.Execute(
            "DELETE FROM Gadgets WHERE Id = @Id AND Version = @ExpectedVersion;",
            new { Id = id, ExpectedVersion = expectedVersion }, transaction);
        if (affected == 0)
        {
            ThrowMissingOrConflict(connection, transaction, id);
        }

        transaction.Commit();
    }

    private static void ThrowMissingOrConflict(SqliteConnection connection, SqliteTransaction transaction, long id)
    {
        var exists = connection.ExecuteScalar<long>(
            "SELECT EXISTS (SELECT 1 FROM Gadgets WHERE Id = @Id);", new { Id = id }, transaction);
        if (exists == 0)
        {
            throw new MissingRecordException("El producto no existe.");
        }

        throw new ConflictException("La versión del producto cambió; vuelva a consultarlo antes de guardar.");
    }

    private static string FormatUtc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static Category ToCategory(CategoryRow row) => new(row.Id, row.Name, ParseUtc(row.CreatedUtc));

    private static Gadget ToGadget(GadgetRow row) =>
        new(row.Id, row.Name, row.Description, row.PriceCents, row.CategoryId, row.Version, ParseUtc(row.CreatedUtc));

    private sealed record CategoryRow(long Id, string Name, string CreatedUtc);

    private sealed record GadgetRow(
        long Id, string Name, string Description, long PriceCents, long CategoryId, long Version, string CreatedUtc);
}
