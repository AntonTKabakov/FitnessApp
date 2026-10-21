using FitnessApp.Data;
using FitnessApp.DTO.TrainerDTOs;
using FitnessApp.Filters;
using FitnessApp.Models;
using FitnessApp.Service.Interfaces;

namespace FitnessApp.Service;

public class TrainerService : ITrainerService
{
    private readonly AppDbContext _context;
    
    public TrainerService(AppDbContext context)
    {
        _context = context;
    }

    public List<TrainerResponseDTO> GetAll(TrainerFilterModel filters)
    {
        IQueryable<Trainer> baseQuery = _context.Trainers;
        
        if (!string.IsNullOrEmpty(filters.Fullname))
        {
            baseQuery = baseQuery.Where(x => x.Fullname.Contains(filters.Fullname));
        }

        if (!string.IsNullOrEmpty(filters.Specialty))
        {
            baseQuery = baseQuery.Where(x => x.Specialty.Contains(filters.Specialty));
        }

        baseQuery = baseQuery.Skip((filters.Page - 1) * filters.PageSize).Take(filters.PageSize);

        IQueryable<TrainerResponseDTO> query = baseQuery.Select(x => new TrainerResponseDTO()
        {
            Fullname = x.Fullname,
            Specialty = x.Specialty
        });

        return query.ToList();
    }

    public TrainerResponseDTO? GetOne(int id)
    {
        TrainerResponseDTO? trainer = _context.Trainers.Where(x => x.Trainerid == id).Select(x => new TrainerResponseDTO()
        {
            Fullname = x.Fullname,
            Specialty = x.Specialty
        }).FirstOrDefault();

        return trainer;
    }

    public int? Create(TrainerEditDTO trainer)
    {
        Trainer entity = new Trainer();
        
        entity.Fullname = trainer.Fullname;
        entity.Specialty = trainer.Specialty;
        entity.Phone = trainer.Phone;

        try
        {
            _context.Trainers.Add(entity);
            _context.SaveChanges();
        }
        catch (Exception e)
        {
            return null;
        }
        
        return entity.Trainerid;
    }

    public bool Update(TrainerEditDTO trainer)
    {
        Trainer? entity = _context.Trainers.Where(x => x.Trainerid == trainer.Id).FirstOrDefault();

        if (entity == null)
        {
            return false;
        }
        
        entity.Fullname = trainer.Fullname;
        entity.Specialty = trainer.Specialty;
        entity.Phone = trainer.Phone;
        
        bool result = _context.SaveChanges() == 1;
        
        return result;
    }

    public bool Delete(int id)
    {
        Trainer? entity = _context.Trainers.Where(x => x.Trainerid == id).FirstOrDefault();

        if (entity == null)
        {
            return false;
        }
        
        _context.Trainers.Remove(entity);
        
        bool result = _context.SaveChanges() == 1;

        return result;
    }
}