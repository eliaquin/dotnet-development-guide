using Store.Api;
using Store.Application;
using Store.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);

var configuredPath = builder.Configuration["Database:Path"] ?? "App_Data/store.db";
var databasePath = Path.GetFullPath(configuredPath, builder.Environment.ContentRootPath);
builder.Services.AddSingleton(new SqliteConnectionFactory(databasePath));
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddScoped<IStoreRepository, SqliteStoreRepository>();
builder.Services.AddScoped<StoreService>();

var app = builder.Build();
app.Services.GetRequiredService<DatabaseInitializer>().Initialize();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", (StoreService service) =>
{
    _ = service.GetCategories(limit: 1);
    return Results.Ok(new { status = "healthy" });
}).ExcludeFromDescription();
app.MapControllers();
app.Run();

// Exposes the generated entry point to WebApplicationFactory without a second host.
public partial class Program;
