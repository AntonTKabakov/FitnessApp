using FitnessApp.Data;
using FitnessApp.DTO;
using FitnessApp.Models;
using FitnessApp.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Controllers.Api;

[ApiController, Route("api/users"), Authorize]
[AutoValidateAntiforgeryToken]
public sealed class UsersApiController(AppDbContext db, CurrentUser current, UserProfileService profiles) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileResponseDTO>> Me(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == current.Id, ct);
        if (user == null) return NotFound();

        return Ok(new UserProfileResponseDTO(user.Id, user.Fullname, user.Email, user.Dateofbirth,
            user.ProfilePicturePath == null ? null : $"~/api/users/{user.Id}/profile-picture?v={Uri.EscapeDataString(user.ProfilePicturePath)}")
        { Role = user.Role });
    }

    [HttpGet("{id:int}/profile-picture")]
    public async Task<IActionResult> Picture(int id, CancellationToken ct)
    {
        if (id != current.Id && !User.IsInRole(UserRoles.Admin)) return NotFound();
        var stream = await profiles.OpenAsync(id, ct);
        return stream == null ? NotFound() : File(stream, "image/webp");
    }

    [HttpPost("me/profile-picture")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file == null) return BadRequest(new { error = "Choose an image to upload." });
        var key = await profiles.ReplaceAsync(current.Id, file, ct);
        return Ok(new { profilePictureUrl = $"~/api/users/{current.Id}/profile-picture?v={Uri.EscapeDataString(key)}" });
    }

    [HttpDelete("me/profile-picture")]
    public async Task<IActionResult> Remove(CancellationToken ct)
    {
        await profiles.RemoveAsync(current.Id, ct);
        return NoContent();
    }
}
