using FitnessApp.Data;
using FitnessApp.DTO.CheckinDTOs;
using FitnessApp.Filters;
using FitnessApp.Models;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Service;

public class CheckinService : ICheckinService
{
    private readonly AppDbContext _context;

    public CheckinService(AppDbContext context)
    {
        _context = context;
    }

    public List<CheckinResponseDTO> GetAll(CheckinFilterModel filters)
    {
        IQueryable<Checkin> baseQuery = _context.Checkins;
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
            .Skip((filters.Page - 1) * filters.PageSize)
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
        CheckinResponseDTO? checkin = _context.Checkins
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

    public async Task<int?> CreateAsync(
    CheckinEditDTO checkin,
    CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
    
        var now = ReceptionTime.Now;
        var today = DateOnly.FromDateTime(now);
    
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);
    
        var customerPass = await _context.Customerpasses
            .FirstOrDefaultAsync(
                x => x.Customerpassid == checkin.CustomerPassId,
                cancellationToken);
    
        if (customerPass == null ||
            customerPass.Expirydate < today ||
            customerPass.Remainingvisits <= 0)
        {
            return null;
        }
    
        bool alreadyCheckedToday = await _context.Checkins.AnyAsync(
            x => x.Customerpassid == checkin.CustomerPassId &&
                 x.Checkindatetime >= dayStart &&
                 x.Checkindatetime < dayEnd,
            cancellationToken);
    
        if (alreadyCheckedToday)
        {
            return null;
        }
    
        var entity = new Checkin
        {
            Customerpassid = checkin.CustomerPassId,
            Checkindatetime = now,
            Trainerid = checkin.TrainerId
        };
    
        customerPass.Remainingvisits--;
    
        _context.Checkins.Add(entity);
    
        await _context.SaveChangesAsync(cancellationToken);
    
        await transaction.CommitAsync(cancellationToken);
    
        return entity.Checkinid;
    }


    public bool Update(CheckinEditDTO checkin)
    {
        Checkin? entity = _context.Checkins
            .Where(x => x.Checkinid == checkin.CheckinId)
            .FirstOrDefault();

        if (entity == null)
        {
            return false;
        }

        entity.Customerpassid = checkin.CustomerPassId;
        entity.Checkindatetime = checkin.CheckinDateTime;
        entity.Trainerid = checkin.TrainerId;

        bool result = _context.SaveChanges() > 0;

        return result;
    }

    public bool Delete(int id)
    {
        Checkin? entity = _context.Checkins
            .Where(x => x.Checkinid == id)
            .FirstOrDefault();

        if (entity == null)
        {
            return false;
        }

        Customerpass? customerPass = _context.Customerpasses
            .Where(x => x.Customerpassid == entity.Customerpassid)
            .FirstOrDefault();

        if (customerPass != null)
        {
            customerPass.Remainingvisits++;
        }

        _context.Checkins.Remove(entity);

        bool result = _context.SaveChanges() > 0;

        return result;
    }
}
