using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Store.Api;
using Store.Infrastructure;
using Xunit;

namespace Store.IntegrationTests;

public sealed class CatalogApiTests
{
    private static readonly string[] WriterNames = ["Writer A", "Writer B"];
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NewDatabaseIsReadyAndHasNoCategories()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var health = await client.GetAsync("/health", Cancellation);
        health.EnsureSuccessStatusCode();
        var categories = await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories", Cancellation);

        Assert.Empty(Assert.IsType<CategoryResponse[]>(categories));
        Assert.True(File.Exists(factory.DatabasePath));
    }

    [Fact]
    public async Task CreatedCategoryHasLocationAndPersistsAcrossClients()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client, "  Audio  ");
        using var otherClient = await factory.CreateAuthenticatedClientAsync();

        var stored = await otherClient.GetFromJsonAsync<CategoryResponse>($"/api/categories/{category.Id}", Cancellation);

        Assert.Equal("Audio", category.Name);
        Assert.Equal(TimeSpan.Zero, category.CreatedUtc.Offset);
        Assert.Equal(category, stored);
    }

    [Fact]
    public async Task DuplicateCategoryReturnsConflictWithoutInsertingAnotherRow()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        _ = await CreateCategory(client, "Audio");

        using var response = await client.PostAsJsonAsync("/api/categories", new { name = "audio" }, Cancellation);

        await AssertProblem(response, HttpStatusCode.Conflict);
        var categories = await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories", Cancellation);
        Assert.Single(Assert.IsType<CategoryResponse[]>(categories));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("This category name exceeds fifty characters by quite a lot")]
    public async Task InvalidCategoryNameReturnsBadRequest(string? name)
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.PostAsJsonAsync("/api/categories", new { name }, Cancellation);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CategoryNameAtMaximumLengthIsAccepted()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();

        var category = await CreateCategory(client, new string('a', 50));

        Assert.Equal(50, category.Name.Length);
    }

    [Fact]
    public async Task MissingCategoryReturnsNotFound()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync("/api/categories/99999", Cancellation);

        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GadgetCanBeCreatedReadUpdatedAndDeleted()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);
        var gadget = await CreateGadget(client, category.Id);
        var read = await client.GetFromJsonAsync<GadgetResponse>($"/api/gadgets/{gadget.Id}", Cancellation);
        Assert.Equal(gadget, read);
        Assert.Equal(19.99m, gadget.Price);
        Assert.Equal(1, gadget.Version);

        using var update = await client.PutAsJsonAsync($"/api/gadgets/{gadget.Id}",
            new { name = "Updated", description = "New description", price = 20.01m, categoryId = category.Id, version = gadget.Version }, Cancellation);
        update.EnsureSuccessStatusCode();
        var updated = Assert.IsType<GadgetResponse>(await update.Content.ReadFromJsonAsync<GadgetResponse>(Cancellation));
        Assert.Equal(gadget.Version + 1, updated.Version);
        Assert.Equal(20.01m, updated.Price);
        Assert.Equal(gadget.CreatedUtc, updated.CreatedUtc);

        using var delete = await client.DeleteAsync($"/api/gadgets/{gadget.Id}?version={updated.Version}", Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var missing = await client.GetAsync($"/api/gadgets/{gadget.Id}", Cancellation);
        await AssertProblem(missing, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnknownCategoryPreventsGadgetCreation()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.PostAsJsonAsync("/api/gadgets",
            new { name = "Headphones", description = "", price = 20m, categoryId = 99999 }, Cancellation);

        await AssertProblem(response, HttpStatusCode.NotFound);
        var gadgets = await client.GetFromJsonAsync<GadgetResponse[]>("/api/gadgets", Cancellation);
        Assert.Empty(Assert.IsType<GadgetResponse[]>(gadgets));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1000000.01")]
    [InlineData("1.001")]
    public async Task InvalidPriceReturnsBadRequest(string price)
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);
        using var content = new StringContent(
            $$"""{"name":"Invalid price","description":"","price":{{price}},"categoryId":{{category.Id}}}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/gadgets", content, Cancellation);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1000000")]
    public async Task PriceBoundariesRoundTripExactly(string price)
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);
        var expected = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        var gadget = await CreateGadget(client, category.Id, price: expected);
        var read = await client.GetFromJsonAsync<GadgetResponse>($"/api/gadgets/{gadget.Id}", Cancellation);

        Assert.Equal(expected, Assert.IsType<GadgetResponse>(read).Price);
    }

    [Fact]
    public async Task LongDescriptionReturnsBadRequest()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);

        using var response = await client.PostAsJsonAsync("/api/gadgets",
            new { name = "Headphones", description = new string('x', 501), price = 1m, categoryId = category.Id }, Cancellation);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StaleUpdateReturnsConflictAndPreservesLatestValue()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);
        var gadget = await CreateGadget(client, category.Id);
        using var first = await client.PutAsJsonAsync($"/api/gadgets/{gadget.Id}",
            new { name = "First update", description = "", price = 4m, categoryId = category.Id, version = gadget.Version }, Cancellation);
        first.EnsureSuccessStatusCode();

        using var stale = await client.PutAsJsonAsync($"/api/gadgets/{gadget.Id}",
            new { name = "Lost update", description = "", price = 5m, categoryId = category.Id, version = gadget.Version }, Cancellation);

        await AssertProblem(stale, HttpStatusCode.Conflict);
        var stored = await client.GetFromJsonAsync<GadgetResponse>($"/api/gadgets/{gadget.Id}", Cancellation);
        Assert.Equal("First update", Assert.IsType<GadgetResponse>(stored).Name);
        Assert.Equal(2, stored.Version);
    }

    [Fact]
    public async Task StaleDeleteReturnsConflictAndKeepsGadget()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);
        var gadget = await CreateGadget(client, category.Id);
        using var update = await client.PutAsJsonAsync($"/api/gadgets/{gadget.Id}",
            new { name = "Updated", description = "", price = 2m, categoryId = category.Id, version = gadget.Version }, Cancellation);
        update.EnsureSuccessStatusCode();

        using var response = await client.DeleteAsync($"/api/gadgets/{gadget.Id}?version={gadget.Version}", Cancellation);

        await AssertProblem(response, HttpStatusCode.Conflict);
        using var stillExists = await client.GetAsync($"/api/gadgets/{gadget.Id}", Cancellation);
        stillExists.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ConcurrentUpdatesWithSameVersionAllowExactlyOneWriter()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);
        var gadget = await CreateGadget(client, category.Id);

        var requests = WriterNames.Select(name => client.PutAsJsonAsync(
            $"/api/gadgets/{gadget.Id}",
            new { name, description = "", price = 2m, categoryId = category.Id, version = gadget.Version }, Cancellation));
        var responses = await Task.WhenAll(requests);
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var stored = await client.GetFromJsonAsync<GadgetResponse>($"/api/gadgets/{gadget.Id}", Cancellation);
            Assert.Equal(2, Assert.IsType<GadgetResponse>(stored).Version);
            Assert.Contains(stored.Name, WriterNames);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task UpdatingMissingGadgetReturnsNotFound()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);

        using var response = await client.PutAsJsonAsync("/api/gadgets/99999",
            new { name = "Missing", description = "", price = 2m, categoryId = category.Id, version = 1 }, Cancellation);

        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletingMissingGadgetReturnsNotFound()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.DeleteAsync("/api/gadgets/99999?version=1", Cancellation);

        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CategoryFilterAndPaginationReturnOnlyRequestedRows()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var firstCategory = await CreateCategory(client, "Audio");
        var secondCategory = await CreateCategory(client, "Video");
        _ = await CreateGadget(client, firstCategory.Id, "First");
        var second = await CreateGadget(client, firstCategory.Id, "Second");
        _ = await CreateGadget(client, secondCategory.Id, "Other category");

        var page = await client.GetFromJsonAsync<GadgetResponse[]>(
            $"/api/gadgets?categoryId={firstCategory.Id}&offset=1&limit=1", Cancellation);

        Assert.Equal(second.Id, Assert.Single(Assert.IsType<GadgetResponse[]>(page)).Id);
    }

    [Theory]
    [InlineData("/api/gadgets?limit=0")]
    [InlineData("/api/gadgets?limit=101")]
    [InlineData("/api/gadgets?offset=-1")]
    [InlineData("/api/gadgets?categoryId=0")]
    [InlineData("/api/categories?offset=-1")]
    [InlineData("/api/gadgets/1?version=0")]
    public async Task InvalidQueryValuesReturnBadRequest(string path)
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var response = path.Contains("version=", StringComparison.Ordinal)
            ? await client.DeleteAsync(path, Cancellation)
            : await client.GetAsync(path, Cancellation);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MalformedJsonReturnsBadRequest()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var content = new StringContent("{\"name\":", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/categories", content, Cancellation);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("POST", "{\"name\":\"Product\",\"categoryId\":1}")]
    [InlineData("POST", "{\"name\":\"Product\",\"price\":1}")]
    [InlineData("PUT", "{\"name\":\"Product\",\"categoryId\":1,\"version\":1}")]
    [InlineData("PUT", "{\"name\":\"Product\",\"price\":1,\"version\":1}")]
    [InlineData("PUT", "{\"name\":\"Product\",\"price\":1,\"categoryId\":1}")]
    public async Task MissingRequiredNumericFieldsReturnBadRequest(string method, string json)
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method),
            method == "POST" ? "/api/gadgets" : "/api/gadgets/1")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, Cancellation);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MaximumCategoryNameWithSurroundingSpacesIsNormalizedBeforeValidation()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var name = new string('c', 50);

        var category = await CreateCategory(client, $"  {name}  ");

        Assert.Equal(name, category.Name);
    }

    [Fact]
    public async Task MaximumGadgetStringsWithSurroundingSpacesAreNormalizedOnCreateAndUpdate()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);
        var name = new string('g', 50);
        var description = new string('d', 500);
        using var create = await client.PostAsJsonAsync("/api/gadgets",
            new { name = $"  {name}  ", description = $"  {description}  ", price = 1m, categoryId = category.Id }, Cancellation);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = Assert.IsType<GadgetResponse>(await create.Content.ReadFromJsonAsync<GadgetResponse>(Cancellation));
        Assert.Equal(name, created.Name);
        Assert.Equal(description, created.Description);

        using var update = await client.PutAsJsonAsync($"/api/gadgets/{created.Id}",
            new { name = $" {name} ", description = $" {description} ", price = 2m, categoryId = category.Id, version = created.Version }, Cancellation);
        update.EnsureSuccessStatusCode();
        var updated = Assert.IsType<GadgetResponse>(await update.Content.ReadFromJsonAsync<GadgetResponse>(Cancellation));
        Assert.Equal(name, updated.Name);
        Assert.Equal(description, updated.Description);
    }

    [Fact]
    public async Task UnexpectedDatabaseFailureReturnsSafeProblemAndLogsException()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        using (var connection = new SqliteConnectionFactory(factory.DatabasePath).OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DROP TABLE Gadgets;";
            _ = command.ExecuteNonQuery();
        }

        using var response = await client.GetAsync("/api/gadgets", Cancellation);

        await AssertProblem(response, HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.DoesNotContain("SQLite", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no such table", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqliteStoreRepository", body, StringComparison.Ordinal);
        Assert.Contains(factory.Logs.Entries, entry =>
            entry.Category == typeof(ApiExceptionHandler).FullName &&
            entry.Level == LogLevel.Error && entry.Exception is SqliteException);
    }

    [Fact]
    public async Task SqlLookingTextIsStoredAsLiteralData()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        const string suspicious = "Audio'); DROP TABLE Categories;--";

        var category = await CreateCategory(client, suspicious);
        var another = await CreateCategory(client, "Still usable");
        var categories = await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories", Cancellation);

        Assert.Equal(suspicious, category.Name);
        Assert.Contains(Assert.IsType<CategoryResponse[]>(categories), item => item.Id == another.Id);
    }

    [Fact]
    public async Task ReinitializingSchemaKeepsExistingData()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);

        var connections = new SqliteConnectionFactory(factory.DatabasePath);
        var initializer = new DatabaseInitializer(connections);
        initializer.Initialize();
        initializer.Initialize();

        var stored = await client.GetFromJsonAsync<CategoryResponse>($"/api/categories/{category.Id}", Cancellation);
        Assert.Equal(category, stored);
    }

    [Fact]
    public async Task InitializerRejectsNewerSchemaWithoutChangingDataOrVersion()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await CreateCategory(client);
        var connections = new SqliteConnectionFactory(factory.DatabasePath);
        using (var connection = connections.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version = 3;";
            _ = command.ExecuteNonQuery();
        }

        var initializer = new DatabaseInitializer(connections);
        Assert.Throws<InvalidOperationException>(initializer.Initialize);

        using (var connection = connections.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(3L, Assert.IsType<long>(command.ExecuteScalar()));
        }

        var stored = await client.GetFromJsonAsync<CategoryResponse>($"/api/categories/{category.Id}", Cancellation);
        Assert.Equal(category, stored);
    }

    [Fact]
    public async Task DevelopmentExposesValidOpenApiDocument()
    {
        await using var factory = new StoreApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync("/openapi/v1.json", Cancellation);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));

        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/gadgets", out _));
        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
    }

    private static async Task<CategoryResponse> CreateCategory(HttpClient client, string name = "Audio")
    {
        using var response = await client.PostAsJsonAsync("/api/categories", new { name }, Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return Assert.IsType<CategoryResponse>(await response.Content.ReadFromJsonAsync<CategoryResponse>(Cancellation));
    }

    private static async Task<GadgetResponse> CreateGadget(HttpClient client, long categoryId, string name = "Headphones", decimal price = 19.99m)
    {
        using var response = await client.PostAsJsonAsync("/api/gadgets",
            new { name, description = "A useful product", price, categoryId }, Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return Assert.IsType<GadgetResponse>(await response.Content.ReadFromJsonAsync<GadgetResponse>(Cancellation));
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("title").GetString()));
    }

    private sealed record CategoryResponse(long Id, string Name, DateTimeOffset CreatedUtc);
    private sealed record GadgetResponse(long Id, string Name, string Description, decimal Price, long CategoryId, long Version, DateTimeOffset CreatedUtc);
}
