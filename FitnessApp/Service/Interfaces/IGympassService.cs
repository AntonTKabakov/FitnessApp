using FitnessApp.DTO.GympassDTOs;
using FitnessApp.Filters;

namespace FitnessApp.Service.Interfaces;

public interface IGympassService
{
    public List<GympassResponseDTO> GetAll(GympassFilterModel filters);
    public GympassResponseDTO? GetOne(int id);
    public int? Create(GympassEditDTO pass);
    public bool Update(GympassEditDTO pass);
    public bool Delete(int id);
}