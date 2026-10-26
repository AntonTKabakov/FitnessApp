
using FitnessApp.DTO;
using Microsoft.AspNetCore.Authorization;
using FitnessApp.Service;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers;

[ApiController]
[AllowAnonymous]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest req,
        CancellationToken cancellationToken)
    {
        var result = await _authService.CreateUser(
            req, cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(new
        {
            message = "Registration successful.",
            result.Id,
            result.Email,
            result.Username
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest req,
        CancellationToken cancellationToken)
    {
        var result = await _authService.Login(
            req, cancellationToken);

        if (!result.IsSuccess)
            return Unauthorized(new { error = result.Error });

        return Ok(new
        {
            message = "Login successful.",
            result.Id,
            result.Email,
            result.Username
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        CancellationToken cancellationToken)
    {
        var result = await _authService.Refresh(
            cancellationToken);

        if (!result.IsSuccess)
            return Unauthorized(new { error = result.Error });

        return Ok(new
        {
            message = "Tokens refreshed successfully.",
            result.Id,
            result.Email,
            result.Username
        });
    }

    [HttpDelete("session")]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken)
    {
        await _authService.Logout(cancellationToken);

        return NoContent();
    }
}
