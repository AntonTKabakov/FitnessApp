using FitnessApp.DTO.CheckinDTOs;
using FitnessApp.Filters;

namespace FitnessApp.Service.Interfaces;

public interface ICheckinService
{
    public List<CheckinResponseDTO> GetAll(CheckinFilterModel filters);

    public CheckinResponseDTO? GetOne(int id);

    public Task<int?> CreateAsync(
        CheckinEditDTO checkin,
        CancellationToken cancellationToken = default);

    public bool Update(CheckinEditDTO checkin);

    public bool Delete(int id);
}