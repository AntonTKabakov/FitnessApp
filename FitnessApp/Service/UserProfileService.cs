using FitnessApp.Data;
using FitnessApp.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Service;

public class UserProfileService(AppDbContext db, IProfilePictureStorageService storage,
    ILogger<UserProfileService> logger)
{
    public async Task<string> ReplaceAsync(int userId, IFormFile file, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new ProfilePictureException(StatusCodes.Status404NotFound, "Account not found.");
        var oldPath = user.ProfilePicturePath;
        var newPath = await storage.SaveAsync(file, ct);
        user.ProfilePicturePath = newPath;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            user.ProfilePicturePath = oldPath;
            await CleanupAsync(newPath);
            throw new ProfilePictureException(StatusCodes.Status409Conflict, "Your picture changed in another request. Reload and try again.");
        }
        catch
        {
            user.ProfilePicturePath = oldPath;
            // A lost database connection can make commit success uncertain. Delete the
            // new file only after confirming that the database does not reference it.
            await CleanupUnreferencedAsync(userId, newPath);
            throw;
        }
        await CleanupAsync(oldPath);
        return newPath;
    }

    public async Task RemoveAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new ProfilePictureException(StatusCodes.Status404NotFound, "Account not found.");
        var oldPath = user.ProfilePicturePath;
        if (oldPath == null) return;
        user.ProfilePicturePath = null;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            user.ProfilePicturePath = oldPath;
            throw new ProfilePictureException(StatusCodes.Status409Conflict, "Your picture changed in another request. Reload and try again.");
        }
        catch
        {
            user.ProfilePicturePath = oldPath;
            throw;
        }
        // Clear the reference first, so a failed delete never leaves a broken reference.
        await CleanupAsync(oldPath);
    }

    public async Task<Stream?> OpenAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.ProfilePicturePath }).SingleOrDefaultAsync(ct)
            ?? throw new ProfilePictureException(StatusCodes.Status404NotFound, "Account not found.");
        return user.ProfilePicturePath == null ? null : await storage.OpenReadAsync(user.ProfilePicturePath, ct);
    }

    private async Task CleanupUnreferencedAsync(int userId, string path)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var referenced = await db.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.ProfilePicturePath == path, timeout.Token);
            if (!referenced) await CleanupAsync(path);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not confirm picture {PictureKey} is unreferenced; retained for storage reconciliation.", path);
        }
    }

    private async Task CleanupAsync(string? path)
    {
        if (path == null) return;
        try
        {
            // Complete cleanup after commit even when the browser disconnects.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await storage.DeleteAsync(path, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not delete unreferenced picture {PictureKey}; storage reconciliation is required.", path);
        }
    }
}
