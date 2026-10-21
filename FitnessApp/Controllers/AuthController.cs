using FitnessApp.Data;
using FitnessApp.DTO;
using FitnessApp.Models;
using FitnessApp.Service;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FitnessApp.Controllers;

public class AuthController : Controller
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<User> _userHasher;
    private readonly AuthService _authService;

    /*private readonly TokenService _tokenService;
private readonly AuthCookieSettings _authCookieSettings;*/


    public AuthController(
        AppDbContext db,
        PasswordHasher<User> userHasher,
        AuthService authService
        /*TokenService tokenService,
        AuthCookieSettings authCookieSettings*/
        )
    {
        _db = db;
        _userHasher = userHasher;
        _authService = authService;
        /*_tokenService = tokenService;
        _authCookieSettings = authCookieSettings;*/
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken cancellationToken)
    {
        var check = _authService.CheckRegisterRequest(req);

        if (check != null)
        {
            return BadRequest(check);
        }

        IActionResult? result = null;
        var strategy = _db.Database.CreateExecutionStrategy();

        /*await strategy.ExecuteAsync(async () =>
        {
            if (await _db.Users.AnyAsync(u => u.Email == email, cancellationToken))
            {
                result = Conflict("Email already exists.");
                return;
            }

            var user = new User
            {
                Email = email,
                Username = string.IsNullOrWhiteSpace(req.Username) ? string.Empty : req.Username.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow
            };

            user.PasswordHash = _userHasher.HashPassword(user, req.Password);

            _db.Users.Add(user);
            await _db.SaveChangesAsync(cancellationToken);

            var session = new Session
            {
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString()
            };

            _db.Session.Add(session);
            await _db.SaveChangesAsync(cancellationToken);

            result = await IssueTokens(user, session, null, cancellationToken);
        });*/

        return result ?? throw new InvalidOperationException("Registration did not produce a response.");
    }
}