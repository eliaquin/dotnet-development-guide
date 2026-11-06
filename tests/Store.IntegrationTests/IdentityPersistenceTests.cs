using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Store.Infrastructure;
using Store.Infrastructure.Identity;
using Xunit;

namespace Store.IntegrationTests;

public sealed class IdentityPersistenceTests
{
    [Fact]
    public void SchemaOneUpgradesToSchemaTwoAndPreservesCatalog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "store-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var connections = new SqliteConnectionFactory(Path.Combine(directory, "catalog.db"));
            using (var connection = connections.OpenConnection())
            using (var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("Store.Infrastructure.Migrations.001_initial.sql"))
            using (var reader = new StreamReader(Assert.IsAssignableFrom<Stream>(stream)))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = reader.ReadToEnd();
                _ = command.ExecuteNonQuery();
                command.CommandText = """
                    INSERT INTO Categories (Name, CreatedUtc) VALUES ('Existing category', '2026-11-06T12:00:00.0000000+00:00');
                    PRAGMA user_version = 1;
                    """;
                _ = command.ExecuteNonQuery();
            }

            var initializer = new DatabaseInitializer(connections);
            initializer.Initialize();
            initializer.Initialize();

            using var verification = connections.OpenConnection();
            using var check = verification.CreateCommand();
            check.CommandText = "PRAGMA user_version;";
            Assert.Equal(2L, Assert.IsType<long>(check.ExecuteScalar()));
            check.CommandText = "SELECT Name FROM Categories;";
            Assert.Equal("Existing category", Assert.IsType<string>(check.ExecuteScalar()));
            check.CommandText = "SELECT count(*) FROM Users;";
            Assert.Equal(0L, Assert.IsType<long>(check.ExecuteScalar()));
            check.CommandText = "SELECT count(*) FROM UserRoles;";
            Assert.Equal(0L, Assert.IsType<long>(check.ExecuteScalar()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StaleUserUpdateCannotOverwriteAccountOrRoles()
    {
        await using var factory = new StoreApiFactory();
        var seeded = await factory.SeedUserAsync("concurrency-user");
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<StoreUser>>();
        var first = Assert.IsType<StoreUser>(await manager.FindByIdAsync(seeded.Id));
        var stale = Assert.IsType<StoreUser>(await manager.FindByIdAsync(seeded.Id));
        first.UserName = "first-writer";
        Assert.True((await manager.UpdateAsync(first)).Succeeded);
        stale.UserName = "second-writer";
        stale.Roles.Clear();
        stale.Roles.Add(StoreRoles.Admin);

        var rejected = await manager.UpdateAsync(stale);
        var stored = Assert.IsType<StoreUser>(await manager.FindByIdAsync(seeded.Id));

        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors, error => error.Code == "ConcurrencyFailure");
        Assert.Equal("first-writer", stored.UserName);
        Assert.Equal([StoreRoles.Reader], stored.Roles);
    }
}
