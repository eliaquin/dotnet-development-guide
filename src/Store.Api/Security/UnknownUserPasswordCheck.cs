using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Store.Infrastructure.Identity;

namespace Store.Api.Security;

// Unknown usernames still incur the configured password-verification work.
public sealed class UnknownUserPasswordCheck
{
    private readonly StoreUser dummyUser = new();
    private readonly PasswordHasher<StoreUser> hasher;
    private readonly string dummyHash;

    public UnknownUserPasswordCheck(IOptions<PasswordHasherOptions> options)
    {
        hasher = new PasswordHasher<StoreUser>(options);
        dummyHash = hasher.HashPassword(dummyUser, Guid.NewGuid().ToString("N"));
    }

    public void Verify(string password) => _ = hasher.VerifyHashedPassword(dummyUser, dummyHash, password);
}
