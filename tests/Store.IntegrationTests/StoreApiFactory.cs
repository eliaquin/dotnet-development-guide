using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Store.Infrastructure.Identity;
using Xunit;

namespace Store.IntegrationTests;

internal sealed class StoreApiFactory(int loginPermitLimit = 100, TimeProvider? clock = null) : WebApplicationFactory<Program>
{
    internal const string Password = "A long test password 42!";
    internal const string CsrfHeader = "X-CSRF-TOKEN";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "store-tests", Guid.NewGuid().ToString("N"));

    public string DatabasePath => Path.Combine(directory, "catalog.db");
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:Path", DatabasePath);
        builder.UseSetting("Authentication:LoginPermitLimit", loginPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Database:Path"] = DatabasePath,
                ["Authentication:LoginPermitLimit"] = loginPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
        if (clock is not null)
        {
            builder.ConfigureServices(services => services.AddSingleton(clock));
        }

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(Logs);
        });
    }

    public HttpClient CreateAnonymousClient(bool handleCookies = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = handleCookies
    });

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string role = StoreRoles.Admin)
    {
        var userName = $"user-{Guid.NewGuid():N}";
        _ = await SeedUserAsync(userName, role);
        var client = CreateAnonymousClient();
        using var login = await LoginAsync(client, userName, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await RefreshCsrfAsync(client);
        return client;
    }

    public async Task<StoreUser> SeedUserAsync(string userName, string role = StoreRoles.Reader, string password = Password)
    {
        await using var scope = Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<StoreUser>>();
        var user = new StoreUser { UserName = userName, LockoutEnabled = true, Roles = { role } };
        var result = await manager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return user;
    }

    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password)
    {
        await RefreshCsrfAsync(client);
        return await client.PostAsJsonAsync("/api/auth/login", new { userName, password }, TestContext.Current.CancellationToken);
    }

    public static async Task RefreshCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf", TestContext.Current.CancellationToken);
        Assert.NotNull(csrf);
        Assert.False(string.IsNullOrWhiteSpace(csrf.Token));
        client.DefaultRequestHeaders.Remove(CsrfHeader);
        client.DefaultRequestHeaders.Add(CsrfHeader, csrf.Token);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record CsrfResponse(string Token);
}
