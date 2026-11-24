using FitnessApp.DTO.CheckinDTOs;
using FitnessApp.Filters;
using FitnessApp.Models;

namespace FitnessApp.Repositories;

public interface ICheckinRepository
{
    List<CheckinResponseDTO> GetAll(CheckinFilterModel filters);
    CheckinResponseDTO? GetOne(int id);
    Checkin? Find(int id);
    Customerpass? FindPass(int id);
    Task<Customerpass?> FindPassAsync(int id, CancellationToken ct);
    bool TrainerExists(int id);
    Task<bool> HasVisitAsync(int passId, DateTime start, DateTime end, CancellationToken ct);
    void Add(Checkin visit);
    void Remove(Checkin visit);
    int SaveChanges();
    Task<int> SaveChangesAsync(CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct);
    T InTransaction<T>(Func<T> operation);
}
