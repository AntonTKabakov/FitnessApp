using FitnessApp.DTO;
using FitnessApp.Service;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers;

[ApiController]
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
            return BadRequest(result.Error);

        return Ok(new { result.Id });
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
}