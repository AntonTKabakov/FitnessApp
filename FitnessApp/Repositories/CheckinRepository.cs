using System.Data;
using FitnessApp.Data;
using FitnessApp.DTO.CheckinDTOs;
using FitnessApp.Filters;
using FitnessApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Repositories;

public class CheckinRepository(AppDbContext db) : ICheckinRepository
{
    public List<CheckinResponseDTO> GetAll(CheckinFilterModel filters)
    {
        IQueryable<Checkin> baseQuery = db.Checkins;
        if (filters.OwnerId.HasValue)
            baseQuery = baseQuery.Where(x => x.Customerpass.Customerid == filters.OwnerId.Value);

        if (filters.CustomerPassId.HasValue)
        {
            baseQuery = baseQuery.Where(x =>
                x.Customerpassid == filters.CustomerPassId.Value);
        }

        if (filters.TrainerId.HasValue)
        {
            baseQuery = baseQuery.Where(x =>
                x.Trainerid == filters.TrainerId.Value);
        }

        if (filters.CheckinDateTime.HasValue)
        {
            baseQuery = baseQuery.Where(x =>
                x.Checkindatetime.Date ==
                filters.CheckinDateTime.Value.Date);
        }

        baseQuery = baseQuery
            .OrderBy(x => x.Checkinid).Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize);

        IQueryable<CheckinResponseDTO> query =
            baseQuery.Select(x => new CheckinResponseDTO()
            {
                CustomerPassId = x.Customerpassid,
                CheckinDateTime = x.Checkindatetime,
                TrainerId = x.Trainerid
            });

        return query.ToList();
    }

    public CheckinResponseDTO? GetOne(int id)
    {
        CheckinResponseDTO? checkin = db.Checkins
            .Where(x => x.Checkinid == id)
            .Select(x => new CheckinResponseDTO()
            {
                CustomerPassId = x.Customerpassid,
                CheckinDateTime = x.Checkindatetime,
                TrainerId = x.Trainerid
            })
            .FirstOrDefault();

        return checkin;
    }

    public Checkin? Find(int id) => db.Checkins.SingleOrDefault(x => x.Checkinid == id);
    public Customerpass? FindPass(int id) => db.Customerpasses.SingleOrDefault(x => x.Customerpassid == id);
    public Task<Customerpass?> FindPassAsync(int id, CancellationToken ct) => db.Customerpasses.SingleOrDefaultAsync(x => x.Customerpassid == id, ct);
    public bool TrainerExists(int id) => db.Trainers.Any(x => x.Trainerid == id);
    public Task<bool> HasVisitAsync(int passId, DateTime start, DateTime end, CancellationToken ct) =>
        db.Checkins.AnyAsync(x => x.Customerpassid == passId && x.Checkindatetime >= start && x.Checkindatetime < end, ct);
    public void Add(Checkin visit) => db.Checkins.Add(visit);
    public void Remove(Checkin visit) => db.Checkins.Remove(visit);
    public int SaveChanges() => db.SaveChanges();
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var result = await operation();
        await transaction.CommitAsync(ct);
        return result;
    }
    public T InTransaction<T>(Func<T> operation)
    {
        using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
        var result = operation();
        transaction.Commit();
        return result;
    }
}
