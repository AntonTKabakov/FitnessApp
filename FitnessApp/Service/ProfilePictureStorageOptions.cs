using System.ComponentModel.DataAnnotations;

namespace FitnessApp.Service;

public class ProfilePictureStorageOptions
{
    public const string SectionName = "FileStorage";

    [Required]
    public string ProfilePicturesPath { get; set; } = "uploads/profile-pictures";

    [Range(1, 5)]
    public int MaxProfilePictureSizeMB { get; set; } = 5;

    [Range(1, 8192)]
    public int MaxImageDimension { get; set; } = 8192;

    [Range(1, 16_000_000)]
    public long MaxDecodedPixels { get; set; } = 16_000_000;

    public long MaxFileSizeBytes => MaxProfilePictureSizeMB * 1024L * 1024L;
}
