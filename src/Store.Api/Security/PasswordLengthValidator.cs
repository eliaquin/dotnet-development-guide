using Microsoft.AspNetCore.Identity;
using Store.Infrastructure.Identity;

namespace Store.Api.Security;

public sealed class PasswordLengthValidator : IPasswordValidator<StoreUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<StoreUser> manager, StoreUser user, string? password) =>
        Task.FromResult(password is { Length: <= 128 }
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordTooLong",
                Description = "La contraseña admite como máximo 128 caracteres."
            }));
}
