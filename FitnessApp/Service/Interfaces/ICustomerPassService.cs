namespace FitnessApp.Service.Interfaces;

public interface ICustomerPassService
{
    Task<int?> PurchaseAsync(int customerId, int passTypeId, CancellationToken cancellationToken);
}
