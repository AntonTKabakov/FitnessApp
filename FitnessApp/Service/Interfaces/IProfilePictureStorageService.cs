namespace FitnessApp.Service.Interfaces;

public interface IProfilePictureStorageService
{
    Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
