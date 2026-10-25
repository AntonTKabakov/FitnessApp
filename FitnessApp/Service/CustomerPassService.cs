using FitnessApp.Data;
using FitnessApp.Models;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Service;

public class CustomerPassService(AppDbContext db) : ICustomerPassService
{
    public async Task<int?> PurchaseAsync(int customerId, int passTypeId, CancellationToken cancellationToken)
    {
        if (!await db.Customers.AnyAsync(x => x.Id == customerId, cancellationToken)) return null;
        var passType = await db.Gympasses.AsNoTracking().SingleOrDefaultAsync(x => x.Passtypeid == passTypeId, cancellationToken);
        var today = ReceptionTime.Today;
        if (passType == null || passType.Maxvisits <= 0 || passType.Validitydays <= 0 || passType.Price < 0 ||
            passType.Validitydays > DateOnly.MaxValue.DayNumber - today.DayNumber) return null;

        var pass = new Customerpass
        {
            Customerid = customerId,
            Passtypeid = passType.Passtypeid,
            Purchasedate = today,
            Expirydate = today.AddDays(passType.Validitydays),
            Remainingvisits = passType.Maxvisits
        };
        db.Customerpasses.Add(pass);
        await db.SaveChangesAsync(cancellationToken);
        return pass.Customerpassid;
    }
}
