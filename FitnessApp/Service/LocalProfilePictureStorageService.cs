using FitnessApp.Service.Interfaces;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace FitnessApp.Service;

public sealed class LocalProfilePictureStorageService : IProfilePictureStorageService
{
    // Keep the bounded decoded image buffers from multiplying with concurrent uploads.
    private static readonly SemaphoreSlim ProcessingSlots = new(2, 2);
    private readonly ProfilePictureStorageOptions options;
    private readonly ILogger<LocalProfilePictureStorageService> logger;
    private readonly string storageDirectory;

    public LocalProfilePictureStorageService(
        IOptions<ProfilePictureStorageOptions> options,
        IWebHostEnvironment environment,
        ILogger<LocalProfilePictureStorageService> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        storageDirectory = Path.GetFullPath(this.options.ProfilePicturesPath, environment.ContentRootPath);
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (storageDirectory.Equals(webRoot, comparison) ||
            storageDirectory.StartsWith(webRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("FileStorage:ProfilePicturesPath must be outside the web root.");
        EnsureSafeDirectory();
    }

    public async Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
            throw new ProfilePictureException(StatusCodes.Status400BadRequest, "Choose a non-empty image.");
        if (file.Length > options.MaxFileSizeBytes)
            throw TooLarge();

        var expectedFormat = ValidateFormat(file);
        string? newPath = null;
        var created = false;
        try
        {
            await ProcessingSlots.WaitAsync(cancellationToken);
            byte[] optimized;
            try
            {
                // Bound compressed buffers as well as decoded pixels for queued uploads.
                // Never rely only on a stream's reported length.
                await using var source = file.OpenReadStream();
                using var input = new MemoryStream();
                var buffer = new byte[81920];
                while (true)
                {
                    var remaining = options.MaxFileSizeBytes - input.Length;
                    var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining + 1)), cancellationToken);
                    if (count == 0) break;
                    if (count > remaining) throw TooLarge();
                    input.Write(buffer, 0, count);
                }
                if (input.Length == 0)
                    throw new ProfilePictureException(StatusCodes.Status400BadRequest, "Choose a non-empty image.");
                cancellationToken.ThrowIfCancellationRequested();
                optimized = Optimize(input.ToArray(), expectedFormat);
            }
            finally
            {
                ProcessingSlots.Release();
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeDirectory();
            Directory.CreateDirectory(storageDirectory);
            EnsureSafeDirectory();
            var key = Guid.NewGuid().ToString("N") + ".webp";
            newPath = Path.Combine(storageDirectory, key);
            await using (var target = new FileStream(newPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous))
            {
                created = true;
                await target.WriteAsync(optimized, cancellationToken);
                await target.FlushAsync(cancellationToken);
            }
            return key;
        }
        catch (Exception exception)
        {
            if (created && newPath != null) TryDeletePartial(newPath);
            if (exception is IOException or UnauthorizedAccessException or DllNotFoundException or TypeInitializationException)
                throw StorageUnavailable(exception);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsStorageKey(path))
        {
            logger.LogWarning("Ignored an invalid stored profile picture identifier.");
            return Task.FromResult<Stream?>(null);
        }

        try
        {
            EnsureSafeDirectory();
            var fullPath = Path.Combine(storageDirectory, path);
            if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                logger.LogWarning("Ignored a symbolic link in profile picture storage.");
                return Task.FromResult<Stream?>(null);
            }
            Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw StorageUnavailable(exception);
        }
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsStorageKey(path))
        {
            logger.LogWarning("Ignored an invalid stored profile picture identifier during cleanup.");
            return Task.CompletedTask;
        }
        try
        {
            EnsureSafeDirectory();
            File.Delete(Path.Combine(storageDirectory, path));
            return Task.CompletedTask;
        }
        catch (DirectoryNotFoundException)
        {
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw StorageUnavailable(exception);
        }
    }

    private byte[] Optimize(byte[] input, SKEncodedImageFormat expectedFormat)
    {
        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        if (codec == null)
            throw InvalidImage();
        if (codec.EncodedFormat != expectedFormat)
            throw new ProfilePictureException(StatusCodes.Status415UnsupportedMediaType, "The image content must match its file extension and MIME type.");

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > options.MaxImageDimension ||
            info.Height > options.MaxImageDimension || (long)info.Width * info.Height > options.MaxDecodedPixels)
            throw new ProfilePictureException(StatusCodes.Status400BadRequest, "The image dimensions exceed the allowed limit.");
        if (codec.FrameCount > 1)
            throw new ProfilePictureException(StatusCodes.Status415UnsupportedMediaType, "Animated profile pictures are not supported. Choose a still image.");

        var decodeInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodeInfo);
        if (decoded.IsEmpty || codec.GetPixels(decodeInfo, decoded.GetPixels()) != SKCodecResult.Success)
            throw InvalidImage();

        var rotated = (int)codec.EncodedOrigin >= (int)SKEncodedOrigin.LeftTop;
        var orientedWidth = rotated ? info.Height : info.Width;
        var orientedHeight = rotated ? info.Width : info.Height;
        var scale = Math.Min(1d, 512d / Math.Max(orientedWidth, orientedHeight));
        var width = Math.Max(1, (int)Math.Round(orientedWidth * scale));
        var height = Math.Max(1, (int)Math.Round(orientedHeight * scale));
        using var output = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(SKColors.Transparent);
            var matrix = OrientationMatrix(codec.EncodedOrigin, info.Width, info.Height,
                (float)width / orientedWidth, (float)height / orientedHeight);
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(decoded, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            canvas.Flush();
        }

        // Encoding fresh pixels discards EXIF, GPS, comments, and all other source metadata.
        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 80);
        if (encoded == null)
            throw new ProfilePictureException(StatusCodes.Status503ServiceUnavailable, "Profile picture processing is temporarily unavailable.");
        return encoded.ToArray();
    }

    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int width, int height, float scaleX, float scaleY)
    {
        // EXIF maps source coordinates into the upright image, including mirrored orientations.
        var (a, b, c, d, e, f) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1, 0, width, 0, 1, 0),
            SKEncodedOrigin.BottomRight => (-1, 0, width, 0, -1, height),
            SKEncodedOrigin.BottomLeft => (1, 0, 0, 0, -1, height),
            SKEncodedOrigin.LeftTop => (0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => (0, -1, height, 1, 0, 0),
            SKEncodedOrigin.RightBottom => (0, -1, height, -1, 0, width),
            SKEncodedOrigin.LeftBottom => (0, 1, 0, -1, 0, width),
            _ => (1, 0, 0, 0, 1, 0)
        };
        return new SKMatrix
        {
            ScaleX = a * scaleX, SkewX = b * scaleX, TransX = c * scaleX,
            SkewY = d * scaleY, ScaleY = e * scaleY, TransY = f * scaleY,
            Persp0 = 0, Persp1 = 0, Persp2 = 1
        };
    }

    private static SKEncodedImageFormat ValidateFormat(IFormFile file)
    {
        var format = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => (SKEncodedImageFormat.Jpeg, "image/jpeg"),
            ".png" => (SKEncodedImageFormat.Png, "image/png"),
            ".webp" => (SKEncodedImageFormat.Webp, "image/webp"),
            _ => throw new ProfilePictureException(StatusCodes.Status415UnsupportedMediaType, "Choose a JPEG, PNG, or WebP image.")
        };
        if (!string.Equals(file.ContentType, format.Item2, StringComparison.OrdinalIgnoreCase))
            throw new ProfilePictureException(StatusCodes.Status415UnsupportedMediaType, "The image MIME type must match its file extension.");
        return format.Item1;
    }

    private static bool IsStorageKey(string? path) => path is { Length: 37 } &&
        path.EndsWith(".webp", StringComparison.Ordinal) && Guid.TryParseExact(path.AsSpan(0, 32), "N", out _);

    private ProfilePictureException TooLarge() => new(StatusCodes.Status413PayloadTooLarge,
        $"Profile pictures must be {options.MaxProfilePictureSizeMB} MB or smaller.");

    private static ProfilePictureException InvalidImage() => new(StatusCodes.Status400BadRequest,
        "The image is corrupted or incomplete. Choose a valid JPEG, PNG, or WebP image.");

    private ProfilePictureException StorageUnavailable(Exception exception)
    {
        logger.LogError(exception, "Profile picture storage or processing failed.");
        return new ProfilePictureException(StatusCodes.Status503ServiceUnavailable,
            "Profile picture storage is temporarily unavailable. Please try again shortly.", exception);
    }

    private void EnsureSafeDirectory()
    {
        for (var directory = new DirectoryInfo(storageDirectory); directory != null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ProfilePictureException(StatusCodes.Status503ServiceUnavailable,
                    "Profile picture storage must use a directory without symbolic links.");
        }
    }

    private void TryDeletePartial(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Unable to remove an incomplete profile picture write.");
        }
    }
}
