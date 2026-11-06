using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Store.Infrastructure;
using Xunit;

namespace Store.IntegrationTests;

public sealed class DatabaseConstraintsTests
{
    [Theory]
    [InlineData("UnknownCategory", 787)]
    [InlineData("NegativePrice", 275)]
    [InlineData("EmptyName", 275)]
    [InlineData("LongDescription", 275)]
    [InlineData("NonpositiveVersion", 275)]
    public async Task SchemaRejectsInvalidDirectWritesWithoutInsertingRows(string invalidField, int expectedError)
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var response = await client.PostAsJsonAsync("/api/categories", new { name = "Audio" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var category = Assert.IsType<CategoryId>(await response.Content.ReadFromJsonAsync<CategoryId>(
            TestContext.Current.CancellationToken));
        using var connection = new SqliteConnectionFactory(factory.DatabasePath).OpenConnection();
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO Gadgets (Name, Description, PriceCents, CategoryId, Version, CreatedUtc)
            VALUES (@Name, @Description, @PriceCents, @CategoryId, @Version, @CreatedUtc);
            """;
        insert.Parameters.AddWithValue("@Name", invalidField == "EmptyName" ? string.Empty : "Headphones");
        insert.Parameters.AddWithValue("@Description", invalidField == "LongDescription" ? new string('x', 501) : "USB");
        insert.Parameters.AddWithValue("@PriceCents", invalidField == "NegativePrice" ? -1L : 1999L);
        insert.Parameters.AddWithValue("@CategoryId", invalidField == "UnknownCategory" ? long.MaxValue : category.Id);
        insert.Parameters.AddWithValue("@Version", invalidField == "NonpositiveVersion" ? 0L : 1L);
        insert.Parameters.AddWithValue("@CreatedUtc", "2026-11-06T12:00:00.0000000+00:00");

        var exception = Assert.Throws<SqliteException>(() => insert.ExecuteNonQuery());

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(expectedError, exception.SqliteExtendedErrorCode);
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT count(*) FROM Gadgets;";
        Assert.Equal(0L, Assert.IsType<long>(count.ExecuteScalar()));
    }

    private sealed record CategoryId(long Id);
}
