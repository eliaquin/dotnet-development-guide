using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Store.Api;
using Store.Api.Security;
using Store.Application;
using Store.Infrastructure;
using Store.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);
// The native MVC antiforgery filter requires the view-feature services, even for JSON controllers.
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();

var configuredPath = builder.Configuration["Database:Path"] ?? "App_Data/store.db";
var databasePath = Path.GetFullPath(configuredPath, builder.Environment.ContentRootPath);
builder.Services.AddSingleton(new SqliteConnectionFactory(databasePath));
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddScoped<IStoreRepository, SqliteStoreRepository>();
builder.Services.AddScoped<StoreService>();

builder.Services.AddIdentityCore<StoreUser>(options =>
{
    options.Password.RequiredLength = 15;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.AllowedForNewUsers = true;
    options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._";
})
    .AddUserStore<DapperUserStore>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<StorePrincipalFactory>()
    .AddPasswordValidator<PasswordLengthValidator>();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
builder.Services.AddSingleton<LoginAttemptLock>();
builder.Services.AddSingleton<UnknownUserPasswordCheck>();
builder.Services.AddScoped<StoreCookieEvents>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "__Host-Store.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = false;
        options.EventsType = typeof(StoreCookieEvents);
    });
builder.Services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
    .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
builder.Services.AddAuthorization(StorePolicies.Configure);
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-Store.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
var keyDirectory = Path.Combine(Path.GetDirectoryName(databasePath)!, "keys");
builder.Services.AddDataProtection().SetApplicationName("Store.Catalog")
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
builder.Services.AddHttpsRedirection(options => options.HttpsPort = builder.Configuration.GetValue("HttpsPort", 7080));
var loginPermitLimit = builder.Configuration.GetValue("Authentication:LoginPermitLimit", 10);
if (loginPermitLimit is < 1 or > 1000)
{
    throw new InvalidOperationException("Authentication:LoginPermitLimit debe estar entre 1 y 1000.");
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();
app.Services.GetRequiredService<DatabaseInitializer>().Initialize();
if (builder.Configuration.GetValue<bool>("Bootstrap:Enabled"))
{
    Environment.ExitCode = await BootstrapAdministrator.RunAsync(app.Services, builder.Configuration);
    await app.DisposeAsync();
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapGet("/health", (StoreService service) =>
{
    _ = service.GetCategories(limit: 1);
    return Results.Ok(new { status = "healthy" });
}).AllowAnonymous().ExcludeFromDescription();
app.MapControllers();
await app.RunAsync();

// Exposes the generated entry point to WebApplicationFactory without a second host.
public partial class Program;
