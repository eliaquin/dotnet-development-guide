using Microsoft.AspNetCore.Identity;
using Store.Infrastructure.Identity;

namespace Store.Api.Security;

public static class BootstrapAdministrator
{
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StoreUser>>();
        var store = (DapperUserStore)scope.ServiceProvider.GetRequiredService<IUserStore<StoreUser>>();
        if (store.HasUsers())
        {
            Console.Error.WriteLine("La creación del administrador inicial requiere una base sin usuarios.");
            return 1;
        }

        var name = configuration["Bootstrap:UserName"];
        var password = configuration["Bootstrap:Password"];
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 50 || password is not { Length: >= 15 and <= 128 })
        {
            Console.Error.WriteLine("Configure Bootstrap__UserName (hasta 50 caracteres) y Bootstrap__Password (15 a 128 caracteres).");
            return 1;
        }

        var user = new StoreUser { UserName = name.Trim(), LockoutEnabled = true, Roles = { StoreRoles.Admin } };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine("No se pudo crear el administrador inicial. Revise el nombre y la política de contraseña.");
            return 1;
        }

        Console.WriteLine("Administrador inicial creado. El servidor no se ha iniciado.");
        return 0;
    }
}
