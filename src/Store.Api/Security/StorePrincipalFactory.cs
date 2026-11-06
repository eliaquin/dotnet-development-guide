using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Store.Infrastructure.Identity;

namespace Store.Api.Security;

public sealed class StorePrincipalFactory(UserManager<StoreUser> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<StoreUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(StoreUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        foreach (var role in await UserManager.GetRolesAsync(user))
        {
            identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, role));
        }

        return identity;
    }
}
