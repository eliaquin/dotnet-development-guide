using Microsoft.AspNetCore.Identity;

namespace Store.Infrastructure.Identity;

public sealed class StoreUser : IdentityUser
{
    public List<string> Roles { get; } = [];
}

public static class StoreRoles
{
    public const string Reader = "Reader";
    public const string Editor = "Editor";
    public const string Admin = "Admin";

    public static bool IsValid(string role) => role is Reader or Editor or Admin;

    internal static string? FromNormalized(string role) => role switch
    {
        "READER" => Reader,
        "EDITOR" => Editor,
        "ADMIN" => Admin,
        _ => null
    };
}
