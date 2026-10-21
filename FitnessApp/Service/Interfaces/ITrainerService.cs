using FitnessApp.DTO.TrainerDTOs;
using FitnessApp.Filters;

namespace FitnessApp.Service.Interfaces;

public interface ITrainerService
{
    public List<TrainerResponseDTO> GetAll(TrainerFilterModel filters);
    public TrainerResponseDTO? GetOne(int id);
    public int? Create(TrainerEditDTO trainer);
    public bool Update(TrainerEditDTO trainer);
    public bool Delete(int id);
}