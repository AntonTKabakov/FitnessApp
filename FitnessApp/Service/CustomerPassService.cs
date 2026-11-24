using FitnessApp.Repositories;
using FitnessApp.Models;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;

namespace FitnessApp.Service;

public class CustomerPassService(ICustomerPassRepository repository) : ICustomerPassService
{
    public async Task<int?> PurchaseAsync(int customerId, int passTypeId, CancellationToken cancellationToken)
    {
        if (!await repository.CustomerExistsAsync(customerId, cancellationToken)) return null;
        var passType = await repository.GetPassTypeAsync(passTypeId, cancellationToken);
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
        return await repository.AddAsync(pass, cancellationToken);
    }
}
