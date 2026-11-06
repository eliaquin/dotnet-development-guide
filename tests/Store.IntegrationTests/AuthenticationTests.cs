using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Store.Infrastructure;
using Store.Infrastructure.Identity;
using Xunit;

namespace Store.IntegrationTests;

public sealed class AuthenticationTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnonymousAntiforgeryEndpointIssuesToken()
    {
        await using var factory = new StoreApiFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/api/auth/csrf", Cancellation);

        Assert.True(response.IsSuccessStatusCode,
            string.Join(Environment.NewLine, factory.Logs.Entries.Where(entry => entry.Exception is not null).Select(entry => entry.Exception)));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("token").GetString()));
    }

    [Theory]
    [InlineData("/api/categories")]
    [InlineData("/api/categories/1")]
    [InlineData("/api/gadgets")]
    [InlineData("/api/gadgets/1")]
    [InlineData("/api/auth/me")]
    public async Task AnonymousRequestsRequireAuthenticationWithoutRedirect(string path)
    {
        await using var factory = new StoreApiFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(path, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(StoreRoles.Reader)]
    [InlineData(StoreRoles.Editor)]
    [InlineData(StoreRoles.Admin)]
    public async Task EverySupportedRoleCanReadTheCatalog(string role)
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(role);

        using var categories = await client.GetAsync("/api/categories", Cancellation);
        using var gadgets = await client.GetAsync("/api/gadgets", Cancellation);
        var profile = await client.GetFromJsonAsync<AccountResponse>("/api/auth/me", Cancellation);

        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
        Assert.Equal(HttpStatusCode.OK, gadgets.StatusCode);
        Assert.Contains(role, Assert.IsType<AccountResponse>(profile).Roles);
    }

    [Theory]
    [InlineData(StoreRoles.Editor)]
    [InlineData(StoreRoles.Admin)]
    public async Task EditorsAndAdministratorsCanWriteTheCatalog(string role)
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(role);

        using var response = await client.PostAsJsonAsync("/api/categories", new { name = "Audio" }, Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/categories")]
    [InlineData("POST", "/api/gadgets")]
    [InlineData("PUT", "/api/gadgets/1")]
    [InlineData("DELETE", "/api/gadgets/1?version=1")]
    [InlineData("POST", "/api/users")]
    public async Task ReaderCannotWriteOrCreatePrivilegedAccounts(string method, string path)
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(StoreRoles.Reader);
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { userName = "escalation", password = StoreApiFactory.Password, role = StoreRoles.Admin })
        };

        using var response = await client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task EditorCannotCreateAccounts()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(StoreRoles.Editor);

        using var response = await client.PostAsJsonAsync("/api/users",
            new { userName = "new-admin", password = StoreApiFactory.Password, role = StoreRoles.Admin }, Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanCreateReaderWhoAuthenticatesWithPersistedRole()
    {
        await using var factory = new StoreApiFactory();
        using var administrator = await factory.CreateAuthenticatedClientAsync();
        using var create = await administrator.PostAsJsonAsync("/api/users",
            new { userName = "catalog-reader", password = StoreApiFactory.Password, role = StoreRoles.Reader }, Cancellation);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = Assert.IsType<AccountResponse>(await create.Content.ReadFromJsonAsync<AccountResponse>(Cancellation));
        Assert.Equal("catalog-reader", created.UserName);
        Assert.Equal([StoreRoles.Reader], created.Roles);
        using var reader = factory.CreateAnonymousClient();

        using var login = await StoreApiFactory.LoginAsync(reader, "catalog-reader", StoreApiFactory.Password);
        var profile = await reader.GetFromJsonAsync<AccountResponse>("/api/auth/me", Cancellation);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(created.Id, Assert.IsType<AccountResponse>(profile).Id);
        Assert.Equal([StoreRoles.Reader], profile.Roles);
    }

    [Fact]
    public async Task AdministratorCannotAssignUnknownRole()
    {
        await using var factory = new StoreApiFactory();
        using var administrator = await factory.CreateAuthenticatedClientAsync();

        using var response = await administrator.PostAsJsonAsync("/api/users",
            new { userName = "unknown-role", password = StoreApiFactory.Password, role = "Owner" }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UserNameUniquenessIsCaseInsensitive()
    {
        await using var factory = new StoreApiFactory();
        using var administrator = await factory.CreateAuthenticatedClientAsync();
        _ = await factory.SeedUserAsync("Existing.User");

        using var duplicate = await administrator.PostAsJsonAsync("/api/users",
            new { userName = "existing.user", password = StoreApiFactory.Password, role = StoreRoles.Reader }, Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(129)]
    public async Task AccountCreationRejectsPasswordOutsideAllowedLength(int length)
    {
        await using var factory = new StoreApiFactory();
        using var administrator = await factory.CreateAuthenticatedClientAsync();

        using var response = await administrator.PostAsJsonAsync("/api/users",
            new { userName = "password-boundary", password = new string('a', length), role = StoreRoles.Reader }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FifteenCharacterPasswordDoesNotRequireCompositionRules()
    {
        await using var factory = new StoreApiFactory();
        using var administrator = await factory.CreateAuthenticatedClientAsync();

        using var response = await administrator.PostAsJsonAsync("/api/users",
            new { userName = "password-minimum", password = new string('a', 15), role = StoreRoles.Reader }, Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task LoginRequiresAntiforgeryToken()
    {
        await using var factory = new StoreApiFactory();
        _ = await factory.SeedUserAsync("csrf-user");
        using var client = factory.CreateAnonymousClient();

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "csrf-user", password = StoreApiFactory.Password }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsafeAuthenticatedRequestRequiresValidAntiforgeryToken(bool useWrongToken)
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Remove(StoreApiFactory.CsrfHeader);
        if (useWrongToken)
        {
            client.DefaultRequestHeaders.Add(StoreApiFactory.CsrfHeader, "not-a-valid-token");
        }

        using var response = await client.PostAsJsonAsync("/api/categories", new { name = "Blocked" }, Cancellation);
        var categories = await client.GetFromJsonAsync<JsonElement[]>("/api/categories", Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Assert.IsType<JsonElement[]>(categories));
    }

    [Fact]
    public async Task TokenFromDifferentCookieCannotAuthorizeWrite()
    {
        await using var factory = new StoreApiFactory();
        using var first = await factory.CreateAuthenticatedClientAsync();
        using var second = await factory.CreateAuthenticatedClientAsync();
        var otherToken = second.DefaultRequestHeaders.GetValues(StoreApiFactory.CsrfHeader).Single();
        first.DefaultRequestHeaders.Remove(StoreApiFactory.CsrfHeader);
        first.DefaultRequestHeaders.Add(StoreApiFactory.CsrfHeader, otherToken);

        using var response = await first.PostAsJsonAsync("/api/categories", new { name = "Blocked" }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousAntiforgeryTokenMustBeRefreshedAfterLogin()
    {
        await using var factory = new StoreApiFactory();
        _ = await factory.SeedUserAsync("csrf-transition", StoreRoles.Admin);
        using var client = factory.CreateAnonymousClient();
        using var login = await StoreApiFactory.LoginAsync(client, "csrf-transition", StoreApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var staleTokenWrite = await client.PostAsJsonAsync("/api/categories", new { name = "Blocked" }, Cancellation);
        await StoreApiFactory.RefreshCsrfAsync(client);
        using var refreshedWrite = await client.PostAsJsonAsync("/api/categories", new { name = "Allowed" }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, staleTokenWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Created, refreshedWrite.StatusCode);
    }

    [Fact]
    public async Task PersistedRoleDowngradeImmediatelyRestrictsExistingCookie()
    {
        await using var factory = new StoreApiFactory();
        var user = await factory.SeedUserAsync("role-transition", StoreRoles.Admin);
        using var client = factory.CreateAnonymousClient();
        using var login = await StoreApiFactory.LoginAsync(client, "role-transition", StoreApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await StoreApiFactory.RefreshCsrfAsync(client);
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<StoreUser>>();
        var stored = Assert.IsType<StoreUser>(await manager.FindByIdAsync(user.Id));
        stored.Roles.Clear();
        stored.Roles.Add(StoreRoles.Reader);
        Assert.True((await manager.UpdateAsync(stored)).Succeeded);

        using var blocked = await client.PostAsJsonAsync("/api/categories", new { name = "Blocked" }, Cancellation);
        using var read = await client.GetAsync("/api/categories", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task InvalidCredentialsHaveSameGenericResultForKnownAndUnknownUsers()
    {
        await using var factory = new StoreApiFactory();
        _ = await factory.SeedUserAsync("existing-user");
        using var client = factory.CreateAnonymousClient();

        using var wrongPassword = await StoreApiFactory.LoginAsync(client, "existing-user", "Wrong long password!");
        using var unknownUser = await StoreApiFactory.LoginAsync(client, "unknown-user", "Wrong long password!");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        using var knownProblem = JsonDocument.Parse(await wrongPassword.Content.ReadAsStringAsync(Cancellation));
        using var unknownProblem = JsonDocument.Parse(await unknownUser.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(knownProblem.RootElement.GetProperty("title").GetString(), unknownProblem.RootElement.GetProperty("title").GetString());
        var knownDetail = knownProblem.RootElement.TryGetProperty("detail", out var firstDetail) ? firstDetail.GetString() : null;
        var unknownDetail = unknownProblem.RootElement.TryGetProperty("detail", out var secondDetail) ? secondDetail.GetString() : null;
        Assert.Equal(knownDetail, unknownDetail);
    }

    [Fact]
    public async Task LoginIssuesSecureHttpOnlyStrictCookieAndDoesNotExposeSecrets()
    {
        await using var factory = new StoreApiFactory();
        _ = await factory.SeedUserAsync("cookie-user");
        using var client = factory.CreateAnonymousClient();

        using var login = await StoreApiFactory.LoginAsync(client, "cookie-user", StoreApiFactory.Password);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authenticationCookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-Store.Auth=", StringComparison.Ordinal));
        Assert.Contains("; secure", authenticationCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", authenticationCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=strict", authenticationCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; path=/", authenticationCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", authenticationCookie, StringComparison.OrdinalIgnoreCase);
        var json = await login.Content.ReadAsStringAsync(Cancellation);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("concurrencyStamp", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TamperedAuthenticationCookieIsRejected()
    {
        await using var factory = new StoreApiFactory();
        using var client = factory.CreateAnonymousClient(handleCookies: false);
        client.DefaultRequestHeaders.Add("Cookie", "__Host-Store.Auth=invalid-cookie-value");

        using var response = await client.GetAsync("/api/auth/me", Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LogoutRevokesOtherSessionsForSameUser()
    {
        await using var factory = new StoreApiFactory();
        _ = await factory.SeedUserAsync("session-user");
        using var first = factory.CreateAnonymousClient();
        using var second = factory.CreateAnonymousClient();
        using var firstLogin = await StoreApiFactory.LoginAsync(first, "session-user", StoreApiFactory.Password);
        using var secondLogin = await StoreApiFactory.LoginAsync(second, "session-user", StoreApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
        await StoreApiFactory.RefreshCsrfAsync(first);

        using var logout = await first.PostAsync("/api/auth/logout", null, Cancellation);
        using var firstProfile = await first.GetAsync("/api/auth/me", Cancellation);
        using var secondProfile = await second.GetAsync("/api/auth/me", Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, firstProfile.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondProfile.StatusCode);
    }

    [Fact]
    public async Task ChangingPasswordRevokesSessionsAndOnlyNewPasswordAuthenticates()
    {
        await using var factory = new StoreApiFactory();
        _ = await factory.SeedUserAsync("password-user");
        using var first = factory.CreateAnonymousClient();
        using var second = factory.CreateAnonymousClient();
        using var firstLogin = await StoreApiFactory.LoginAsync(first, "password-user", StoreApiFactory.Password);
        using var secondLogin = await StoreApiFactory.LoginAsync(second, "password-user", StoreApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
        await StoreApiFactory.RefreshCsrfAsync(first);
        const string newPassword = "An entirely new test password!";

        using var change = await first.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = StoreApiFactory.Password, newPassword }, Cancellation);
        using var otherSession = await second.GetAsync("/api/auth/me", Cancellation);
        using var oldLogin = await StoreApiFactory.LoginAsync(first, "password-user", StoreApiFactory.Password);
        using var newLogin = await StoreApiFactory.LoginAsync(first, "password-user", newPassword);

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherSession.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task WrongCurrentPasswordDoesNotChangeCredentials()
    {
        await using var factory = new StoreApiFactory();
        _ = await factory.SeedUserAsync("unchanged-user");
        using var client = factory.CreateAnonymousClient();
        using var login = await StoreApiFactory.LoginAsync(client, "unchanged-user", StoreApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await StoreApiFactory.RefreshCsrfAsync(client);

        using var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "Wrong current password!", newPassword = "A different new password!" }, Cancellation);
        using var validLogin = await StoreApiFactory.LoginAsync(client, "unchanged-user", StoreApiFactory.Password);

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, validLogin.StatusCode);
    }

    [Fact]
    public async Task RepeatedWrongCurrentPasswordsLockAccountAndRevokeOtherSessions()
    {
        await using var factory = new StoreApiFactory();
        _ = await factory.SeedUserAsync("change-lockout");
        using var first = factory.CreateAnonymousClient();
        using var second = factory.CreateAnonymousClient();
        using var firstLogin = await StoreApiFactory.LoginAsync(first, "change-lockout", StoreApiFactory.Password);
        using var secondLogin = await StoreApiFactory.LoginAsync(second, "change-lockout", StoreApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
        await StoreApiFactory.RefreshCsrfAsync(first);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await first.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = "Wrong current password!", newPassword = "An entirely new password!" }, Cancellation);
            Assert.Equal(HttpStatusCode.BadRequest, failure.StatusCode);
        }

        using var firstSession = await first.GetAsync("/api/auth/me", Cancellation);
        using var otherSession = await second.GetAsync("/api/auth/me", Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, firstSession.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherSession.StatusCode);
    }

    [Fact]
    public async Task FailedAttemptsLockAccountUntilLockoutExpires()
    {
        await using var factory = new StoreApiFactory();
        var user = await factory.SeedUserAsync("locked-user");
        using var client = factory.CreateAnonymousClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await StoreApiFactory.LoginAsync(client, "locked-user", "Wrong long password!");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var lockedLogin = await StoreApiFactory.LoginAsync(client, "locked-user", StoreApiFactory.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, lockedLogin.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<StoreUser>>();
        var stored = Assert.IsType<StoreUser>(await manager.FindByIdAsync(user.Id));
        Assert.True(await manager.IsLockedOutAsync(stored));
        var unlock = await manager.SetLockoutEndDateAsync(stored, DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(unlock.Succeeded);

        using var afterExpiry = await StoreApiFactory.LoginAsync(client, "locked-user", StoreApiFactory.Password);

        Assert.Equal(HttpStatusCode.OK, afterExpiry.StatusCode);
    }

    [Fact]
    public async Task ConcurrentFailuresCannotBypassAccountLockout()
    {
        await using var factory = new StoreApiFactory();
        _ = await factory.SeedUserAsync("concurrent-user");
        var clients = Enumerable.Range(0, 5).Select(_ => factory.CreateAnonymousClient()).ToArray();
        try
        {
            var attempts = clients.Select(async (client, index) =>
            {
                using var response = await StoreApiFactory.LoginAsync(client,
                    index % 2 == 0 ? "concurrent-user" : "CONCURRENT-USER", "Wrong long password!");
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            });
            await Task.WhenAll(attempts);
            using var validClient = factory.CreateAnonymousClient();

            using var locked = await StoreApiFactory.LoginAsync(validClient, "concurrent-user", StoreApiFactory.Password);

            Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task LoginRateLimitReturnsTooManyRequests()
    {
        await using var factory = new StoreApiFactory(loginPermitLimit: 2);
        using var client = factory.CreateAnonymousClient();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var failure = await StoreApiFactory.LoginAsync(client, "unknown-user", "Wrong long password!");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var limited = await StoreApiFactory.LoginAsync(client, "unknown-user", "Wrong long password!");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task PersistedPasswordIsSaltedHashVerifiedByIdentity()
    {
        await using var factory = new StoreApiFactory();
        var user = await factory.SeedUserAsync("hash-user");
        using var connection = new SqliteConnectionFactory(factory.DatabasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT PasswordHash FROM Users WHERE Id = @Id;";
        command.Parameters.AddWithValue("@Id", user.Id);
        var hash = Assert.IsType<string>(command.ExecuteScalar());
        await using var scope = factory.Services.CreateAsyncScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<StoreUser>>();

        Assert.NotEqual(StoreApiFactory.Password, hash);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, hash, StoreApiFactory.Password));
    }

    [Fact]
    public async Task ExpiredAuthenticationTicketIsRejected()
    {
        var clock = new TestClock();
        await using var factory = new StoreApiFactory(clock: clock);
        using var client = await factory.CreateAuthenticatedClientAsync();
        clock.Advance(TimeSpan.FromMinutes(31));

        using var response = await client.GetAsync("/api/auth/me", Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record AccountResponse(string Id, string UserName, string[] Roles);

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now += duration;
    }
}
