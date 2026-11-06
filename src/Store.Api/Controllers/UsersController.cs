using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Store.Api.Security;
using Store.Infrastructure.Identity;

namespace Store.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = StorePolicies.ManageUsers)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(16_384)]
public sealed class UsersController(UserManager<StoreUser> users) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request)
    {
        if (!StoreRoles.IsValid(request.Role))
        {
            return Problem(statusCode: 400, title: "El rol debe ser Reader, Editor o Admin.");
        }

        var user = new StoreUser { UserName = request.UserName.Trim(), LockoutEnabled = true, Roles = { request.Role } };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var status = result.Errors.Any(error => error.Code == "DuplicateUserName") ? 409 : 400;
            return Problem(statusCode: status, title: "No se pudo crear el usuario.", detail: "Revise el nombre y la política de contraseña.");
        }

        return StatusCode(StatusCodes.Status201Created, UserResponse.From(user));
    }
}
