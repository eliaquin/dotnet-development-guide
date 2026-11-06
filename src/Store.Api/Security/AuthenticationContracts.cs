using System.ComponentModel.DataAnnotations;
using Store.Infrastructure.Identity;

namespace Store.Api.Security;

public sealed class LoginRequest
{
    [Required, StringLength(50)]
    public string UserName { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed class CreateUserRequest
{
    [Required, StringLength(50)]
    public string UserName { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 15)]
    public string Password { get; init; } = string.Empty;

    [Required]
    public string Role { get; init; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required, StringLength(128)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 15)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed record UserResponse(string Id, string UserName, IReadOnlyList<string> Roles)
{
    public static UserResponse From(StoreUser user) => new(user.Id, user.UserName!, user.Roles.ToArray());
}
