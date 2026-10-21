using FitnessApp.Data;
using FitnessApp.DTO.GympassDTOs;
using FitnessApp.DTO.TrainerDTOs;
using FitnessApp.Filters;
using FitnessApp.Models;
using FitnessApp.Service.Interfaces;

namespace FitnessApp.Service;

public class GympassService : IGympassService
{
    private readonly AppDbContext _context;
    
    public GympassService(AppDbContext context)
    {
        _context = context;
    }

    public List<GympassResponseDTO> GetAll(GympassFilterModel filters)
    {
        IQueryable<Gympass> baseQuery = _context.Gympasses;
        
        if (!string.IsNullOrEmpty(filters.PassName))
        {
            baseQuery = baseQuery.Where(x => x.Passname.Contains(filters.PassName));
        }

        if (filters.Price.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Price == filters.Price.Value);
        }

        if (filters.MaxVisits.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Maxvisits == filters.MaxVisits.Value);
        }
        
        if (filters.ValidityDays.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Validitydays == filters.ValidityDays.Value);
        }

        baseQuery = baseQuery.Skip((filters.Page - 1) * filters.PageSize).Take(filters.PageSize);

        IQueryable<GympassResponseDTO> query = baseQuery.Select(x => new GympassResponseDTO()
        {
            PassName = x.Passname,
            Price = x.Price,
            MaxVisits = x.Maxvisits,
            ValidityDays = x.Validitydays
        });

        return query.ToList();
    }

    public GympassResponseDTO? GetOne(int id)
    {
        GympassResponseDTO? pass = _context.Gympasses.Where(x => x.Passtypeid == id).Select(x => new GympassResponseDTO()
        {
            PassName = x.Passname,
            Price = x.Price,
            MaxVisits = x.Maxvisits,
            ValidityDays = x.Validitydays
        }).FirstOrDefault();

        return pass;
    }

    public int? Create(GympassEditDTO pass)
    {
        Gympass entity = new Gympass();
        
        entity.Passname = pass.PassName;
        entity.Price = pass.Price;
        entity.Maxvisits = pass.MaxVisits;
        entity.Validitydays = pass.ValidityDays;
        
        
        try
        {
            _context.Gympasses.Add(entity);
            _context.SaveChanges();
        }
        catch (Exception e)
        {
            return null;
        }
        
        return entity.Passtypeid;
    }

    public bool Update(GympassEditDTO pass)
    {
        Gympass? entity = _context.Gympasses.Where(x => x.Passtypeid == pass.PassTypeId).FirstOrDefault();

        if (entity == null)
        {
            return false;
        }
        
        entity.Passname = pass.PassName;
        entity.Price = pass.Price;
        entity.Maxvisits = pass.MaxVisits;
        entity.Validitydays = pass.ValidityDays;
        
        bool result = _context.SaveChanges() == 1;
        
        return result;
    }

    public bool Delete(int id)
    {
        Gympass? entity = _context.Gympasses.Where(x => x.Passtypeid == id).FirstOrDefault();

        if (entity == null)
        {
            return false;
        }
        
        _context.Gympasses.Remove(entity);
        
        bool result = _context.SaveChanges() == 1;

        return result;
    }
}