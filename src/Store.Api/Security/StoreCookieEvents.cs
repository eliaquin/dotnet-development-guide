using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Store.Infrastructure.Identity;

namespace Store.Api.Security;

public sealed class StoreCookieEvents(
    UserManager<StoreUser> users,
    IUserClaimsPrincipalFactory<StoreUser> principals,
    IOptions<IdentityOptions> identityOptions) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var user = context.Principal is null ? null : await users.GetUserAsync(context.Principal);
        var stamp = context.Principal?.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType);
        if (user is null || string.IsNullOrEmpty(stamp) ||
            !string.Equals(stamp, await users.GetSecurityStampAsync(user), StringComparison.Ordinal) ||
            await users.IsLockedOutAsync(user))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        // Authorization always observes the current persisted roles, not stale cookie claims.
        context.ReplacePrincipal(await principals.CreateAsync(user));
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
