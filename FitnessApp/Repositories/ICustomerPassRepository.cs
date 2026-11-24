using FitnessApp.Models;

namespace FitnessApp.Repositories;

public interface ICustomerPassRepository
{
    Task<bool> CustomerExistsAsync(int id, CancellationToken ct);
    Task<Gympass?> GetPassTypeAsync(int id, CancellationToken ct);
    Task<int> AddAsync(Customerpass pass, CancellationToken ct);
}
