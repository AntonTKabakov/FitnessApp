using FitnessApp.DTO.CheckinDTOs;
using FitnessApp.Filters;
using FitnessApp.Models;
using FitnessApp.Repositories;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;

namespace FitnessApp.Service;

public class CheckinService(ICheckinRepository repository) : ICheckinService
{
    public List<CheckinResponseDTO> GetAll(CheckinFilterModel filters) => repository.GetAll(filters);
    public CheckinResponseDTO? GetOne(int id) => repository.GetOne(id);

    public Task<int?> CreateAsync(CheckinEditDTO checkin, CancellationToken cancellationToken = default) =>
        repository.InTransactionAsync<int?>(async () =>
        {
            var now = ReceptionTime.Now;
            var pass = await repository.FindPassAsync(checkin.CustomerPassId, cancellationToken);
            if (pass == null || pass.Expirydate < DateOnly.FromDateTime(now) || pass.Remainingvisits <= 0)
                return null;
            if (checkin.TrainerId.HasValue && !repository.TrainerExists(checkin.TrainerId.Value)) return null;
            if (await repository.HasVisitAsync(checkin.CustomerPassId, now.Date, now.Date.AddDays(1), cancellationToken))
                return null;
            var entity = new Checkin { Customerpassid = checkin.CustomerPassId, Checkindatetime = now, Trainerid = checkin.TrainerId };
            pass.Remainingvisits--;
            repository.Add(entity);
            await repository.SaveChangesAsync(cancellationToken);
            return entity.Checkinid;
        }, cancellationToken);

    public bool Update(CheckinEditDTO checkin)
    {
        if (!checkin.CheckinId.HasValue) return false;
        var entity = repository.Find(checkin.CheckinId.Value);
        if (entity == null) return false;
        // Changing membership or time would bypass allowance and daily-entry validation.
        if (entity.Customerpassid != checkin.CustomerPassId || entity.Checkindatetime != checkin.CheckinDateTime) return false;
        if (checkin.TrainerId.HasValue && !repository.TrainerExists(checkin.TrainerId.Value)) return false;
        if (entity.Trainerid == checkin.TrainerId) return true;
        entity.Trainerid = checkin.TrainerId;
        return repository.SaveChanges() > 0;
    }

    public bool Delete(int id) => repository.InTransaction(() =>
    {
        var entity = repository.Find(id);
        if (entity == null) return false;
        var pass = repository.FindPass(entity.Customerpassid);
        if (pass != null) pass.Remainingvisits++;
        repository.Remove(entity);
        return repository.SaveChanges() > 0;
    });
}
