using System.Data;
using FitnessApp.Data;
using FitnessApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Service;

public class AccountRoleService(AppDbContext db)
{
    public async Task<string?> ChangeAsync(int actorId, int accountId, string role, CancellationToken ct)
    {
        if (!UserRoles.IsValid(role)) return "Choose Admin or User.";
        if (actorId == accountId && role != UserRoles.Admin) return "Ask another administrator to change your role.";
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actorIsAdmin = await db.Users.AnyAsync(x => x.Id == actorId && x.Role == UserRoles.Admin, ct);
        if (!actorIsAdmin) return "Your account no longer has administrator access.";
        var account = await db.Users.SingleOrDefaultAsync(x => x.Id == accountId, ct);
        if (account == null) return "This account no longer exists.";
        if (account.Role == UserRoles.Admin && role == UserRoles.User &&
            !await db.Users.AnyAsync(x => x.Id != accountId && x.Role == UserRoles.Admin, ct))
            return "At least one administrator must remain.";
        account.Role = role;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }
}
