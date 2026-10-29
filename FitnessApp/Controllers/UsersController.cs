using FitnessApp.Data;
using FitnessApp.DTO;
using FitnessApp.Filters;
using FitnessApp.Service;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
[TypeFilter(typeof(ProfilePictureExceptionFilter))]
public class UsersController(UserProfileService profiles, CurrentUser currentUser, AppDbContext db,
    IAntiforgery antiforgery, IWebHostEnvironment environment) : ControllerBase
{
    // Leaves room for multipart headers while enforcing the actual file limit in storage.
    private const long MaxMultipartBytes = ProfilePictureUploadLimitFilter.MaxMultipartBytes;

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentUser.Id, ct);
        if (user == null) return NotFound(new { error = "Account not found." });
        Response.Headers["RequestVerificationToken"] = antiforgery.GetAndStoreTokens(HttpContext).RequestToken;
        return Ok(new UserProfileResponseDTO(user.Id, user.Fullname, user.Email, user.Dateofbirth,
            user.ProfilePicturePath == null ? null : PictureUrl(user.Id, user.ProfilePicturePath)));
    }

    [HttpPost("me/profile-picture")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxMultipartBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxMultipartBytes)]
    [TypeFilter(typeof(ProfilePictureAntiforgeryFilter))]
    [TypeFilter(typeof(ProfilePictureUploadLimitFilter))]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file == null) return BadRequest(new { error = "Select an image to upload." });
        var path = await profiles.ReplaceAsync(currentUser.Id, file, ct);
        return Ok(new { profilePictureUrl = PictureUrl(currentUser.Id, path) });
    }

    [HttpDelete("me/profile-picture")]
    [TypeFilter(typeof(ProfilePictureAntiforgeryFilter))]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        await profiles.RemoveAsync(currentUser.Id, ct);
        return NoContent();
    }

    [HttpGet("{id:int:min(1)}/profile-picture")]
    public async Task<IActionResult> Picture(int id, CancellationToken ct)
    {
        // Match the existing private profiles: a member may read only their own;
        // administrators may inspect customer pictures. Conceal cross-account IDs.
        if (id != currentUser.Id && !currentUser.IsAdmin) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var image = await profiles.OpenAsync(id, ct);
        if (image != null) return File(image, "image/webp");
        return PhysicalFile(Path.Combine(environment.WebRootPath, "images", "default-avatar.svg"), "image/svg+xml");
    }

    private string PictureUrl(int userId, string key) =>
        Url.Content($"~/api/users/{userId}/profile-picture") + QueryString.Create("v", key);
}
