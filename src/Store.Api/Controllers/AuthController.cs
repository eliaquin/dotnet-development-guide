using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Store.Api.Security;
using Store.Infrastructure.Identity;

namespace Store.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(16_384)]
public sealed class AuthController(
    UserManager<StoreUser> users,
    SignInManager<StoreUser> signIn,
    LoginAttemptLock attempts,
    UnknownUserPasswordCheck unknownUserPasswordCheck) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult GetCsrf([FromServices] IAntiforgery antiforgery) => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<UserResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var name = request.UserName.Trim();
        var gate = attempts.For(users.NormalizeName(name));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var user = await users.FindByNameAsync(name);
            if (user is null)
            {
                unknownUserPasswordCheck.Verify(request.Password);
                return InvalidCredentials();
            }

            var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                return InvalidCredentials();
            }

            await signIn.SignInAsync(user, isPersistent: false);
            return Ok(UserResponse.From(user));
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> Me()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(UserResponse.From(user));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            return Problem(statusCode: 409, title: "La cuenta cambió. Vuelva a intentar cerrar la sesión.");
        }

        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var name = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return Unauthorized();
        }

        var gate = attempts.For(users.NormalizeName(name));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var user = await users.GetUserAsync(User);
            if (user is null)
            {
                return Unauthorized();
            }

            var check = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
            if (!check.Succeeded)
            {
                return PasswordChangeFailed();
            }

            var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded)
            {
                return PasswordChangeFailed();
            }

            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return NoContent();
        }
        finally
        {
            gate.Release();
        }
    }

    private ObjectResult InvalidCredentials() => Problem(statusCode: 401, title: "Credenciales inválidas.");

    private ObjectResult PasswordChangeFailed() => Problem(statusCode: 400, title: "No se pudo cambiar la contraseña.",
        detail: "Verifique la contraseña actual y la política de contraseña.");
}
