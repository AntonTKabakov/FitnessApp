using FitnessApp.Data;
using FitnessApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Repositories;

public class CustomerPassRepository(AppDbContext db) : ICustomerPassRepository
{
    public Task<bool> CustomerExistsAsync(int id, CancellationToken ct) => db.Customers.AnyAsync(x => x.Id == id, ct);
    public Task<Gympass?> GetPassTypeAsync(int id, CancellationToken ct) =>
        db.Gympasses.AsNoTracking().SingleOrDefaultAsync(x => x.Passtypeid == id, ct);
    public async Task<int> AddAsync(Customerpass pass, CancellationToken ct)
    {
        db.Customerpasses.Add(pass);
        await db.SaveChangesAsync(ct);
        return pass.Customerpassid;
    }
}
