using FitnessApp.DTO;
using FitnessApp.Service;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FitnessApp.Controllers.Api;

[ApiController, Route("api/session"), AllowAnonymous, AutoValidateAntiforgeryToken]
public class SessionController(AuthService auth, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("antiforgery")]
    public IActionResult Antiforgery() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.Login(request, ct);
        return result.IsSuccess ? Ok(new { result.Id, result.Email, result.Username }) : Unauthorized(new { error = result.Error });
    }

    [HttpPost("registration")]
    public async Task<IActionResult> Register(RegisterViewModel request, CancellationToken ct)
    {
        var result = await auth.CreateUser(new RegisterRequest { Username = request.FullName,
            Email = request.Email, Password = request.Password, Dateofbirth = request.DateOfBirth!.Value }, ct);
        return result.IsSuccess ? Ok(new { result.Id, result.Email, result.Username }) : BadRequest(new { error = result.Error });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var result = await auth.Refresh(ct);
        return result.IsSuccess ? Ok(new { result.Id, result.Email, result.Username }) : Unauthorized(new { error = result.Error });
    }

    [HttpDelete]
    public async Task<IActionResult> Logout(CancellationToken ct) { await auth.Logout(ct); return NoContent(); }
}
