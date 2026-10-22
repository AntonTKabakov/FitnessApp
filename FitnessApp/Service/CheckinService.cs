using FitnessApp.Data;
using FitnessApp.DTO.CheckinDTOs;
using FitnessApp.Filters;
using FitnessApp.Models;
using FitnessApp.Service.Interfaces;

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

    public int? Create(CheckinEditDTO checkin)
    {
        Customerpass? customerPass = _context.Customerpasses
            .Where(x => x.Customerpassid == checkin.CustomerPassId)
            .FirstOrDefault();

        if (customerPass == null)
        {
            return null;
        }

        if (customerPass.Expirydate < DateOnly.FromDateTime(DateTime.Now))
        {
            return null;
        }

        if (customerPass.Remainingvisits <= 0)
        {
            return null;
        }

        bool alreadyCheckedToday = _context.Checkins.Any(x =>
            x.Customerpassid == checkin.CustomerPassId &&
            x.Checkindatetime.Date == DateTime.Now.Date);

        if (alreadyCheckedToday)
        {
            return null;
        }

        Checkin entity = new Checkin();

        entity.Customerpassid = checkin.CustomerPassId;
        entity.Checkindatetime = DateTime.Now;
        entity.Trainerid = checkin.TrainerId;

        customerPass.Remainingvisits--;

        try
        {
            _context.Checkins.Add(entity);
            _context.SaveChanges();
        }
        catch (Exception)
        {
            return null;
        }

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