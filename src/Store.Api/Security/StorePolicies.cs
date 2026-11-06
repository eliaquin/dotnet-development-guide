using Store.Infrastructure.Identity;

namespace Store.Api.Security;

public static class StorePolicies
{
    public const string ReadCatalog = nameof(ReadCatalog);
    public const string WriteCatalog = nameof(WriteCatalog);
    public const string ManageUsers = nameof(ManageUsers);

    public static void Configure(Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
    {
        options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build();
        options.AddPolicy(ReadCatalog, policy => policy.RequireAuthenticatedUser().RequireRole(StoreRoles.Reader, StoreRoles.Editor, StoreRoles.Admin));
        options.AddPolicy(WriteCatalog, policy => policy.RequireAuthenticatedUser().RequireRole(StoreRoles.Editor, StoreRoles.Admin));
        options.AddPolicy(ManageUsers, policy => policy.RequireAuthenticatedUser().RequireRole(StoreRoles.Admin));
    }
}
