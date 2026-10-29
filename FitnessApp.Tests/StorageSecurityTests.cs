using FitnessApp.Service;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace FitnessApp.Tests;

public sealed class StorageSecurityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fitness-storage-security-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Actual_stream_size_is_bounded_when_reported_file_length_is_wrong()
    {
        var storage = CreateStorage();
        var file = new LengthLyingFile(new byte[5 * 1024 * 1024 + 1]);
        var error = await Assert.ThrowsAsync<ProfilePictureException>(() => storage.SaveAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, error.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(root, "pictures")));
    }

    [Fact]
    public async Task Actual_filesystem_write_failure_returns_safe_503_and_preserves_existing_file()
    {
        var storage = CreateStorage();
        var blockedDirectory = Path.Combine(root, "pictures");
        await File.WriteAllTextAsync(blockedDirectory, "existing file must survive", TestContext.Current.CancellationToken);
        using var bitmap = new SKBitmap(20, 20);
        bitmap.Erase(SKColors.Green);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        using var input = new MemoryStream(encoded.ToArray());
        var file = new FormFile(input, 0, input.Length, "file", "portrait.png")
        {
            Headers = new HeaderDictionary(), ContentType = "image/png"
        };
        var error = await Assert.ThrowsAsync<ProfilePictureException>(() => storage.SaveAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);
        Assert.DoesNotContain(root, error.Message);
        Assert.Equal("existing file must survive", await File.ReadAllTextAsync(blockedDirectory, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(blockedDirectory));
    }

    [Theory]
    [InlineData("wwwroot")]
    [InlineData("wwwroot/private-pictures")]
    public void Configured_web_root_storage_is_rejected(string path)
    {
        Assert.Throws<InvalidOperationException>(() => CreateStorage(path));
    }

    [Fact]
    public async Task Animated_webp_is_rejected_before_storage()
    {
        using var first = new SKBitmap(20, 20);
        using var second = new SKBitmap(20, 20);
        first.Erase(SKColors.Red);
        second.Erase(SKColors.Blue);
        SKWebpEncoderFrame[] frames =
        [
            new(first, TimeSpan.FromMilliseconds(100)),
            new(second, TimeSpan.FromMilliseconds(100))
        ];
        using var encoded = SKWebpEncoder.EncodeAnimated(frames, SKWebpEncoderOptions.Default);
        Assert.NotNull(encoded);
        using var input = new MemoryStream(encoded.ToArray());
        var file = new FormFile(input, 0, input.Length, "file", "animated.webp")
        {
            Headers = new HeaderDictionary(), ContentType = "image/webp"
        };
        var error = await Assert.ThrowsAsync<ProfilePictureException>(() => CreateStorage().SaveAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, error.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(root, "pictures")));
    }

    [Fact]
    public async Task Symbolic_link_files_cannot_expose_or_delete_their_target()
    {
        var storage = CreateStorage();
        var pictures = Path.Combine(root, "pictures");
        Directory.CreateDirectory(pictures);
        var target = Path.Combine(root, "private.txt");
        await File.WriteAllTextAsync(target, "private target", TestContext.Current.CancellationToken);
        var key = Guid.NewGuid().ToString("N") + ".webp";
        CreateFileLinkOrSkip(Path.Combine(pictures, key), target);
        await using var image = await storage.OpenReadAsync(key, TestContext.Current.CancellationToken);
        Assert.Null(image);
        await storage.DeleteAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal("private target", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Existing_symbolic_link_storage_directory_is_rejected()
    {
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "private");
        Directory.CreateDirectory(target);
        CreateDirectoryLinkOrSkip(Path.Combine(root, "pictures"), target);
        var error = Assert.Throws<ProfilePictureException>(() => CreateStorage());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);
    }

    [Fact]
    public async Task Directory_replaced_by_a_symbolic_link_cannot_read_or_delete_external_files()
    {
        var storage = CreateStorage();
        var target = Path.Combine(root, "private");
        Directory.CreateDirectory(target);
        var key = Guid.NewGuid().ToString("N") + ".webp";
        var targetFile = Path.Combine(target, key);
        await File.WriteAllTextAsync(targetFile, "private target", TestContext.Current.CancellationToken);
        CreateDirectoryLinkOrSkip(Path.Combine(root, "pictures"), target);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            (await Assert.ThrowsAsync<ProfilePictureException>(() => storage.OpenReadAsync(key, TestContext.Current.CancellationToken))).StatusCode);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            (await Assert.ThrowsAsync<ProfilePictureException>(() => storage.DeleteAsync(key, TestContext.Current.CancellationToken))).StatusCode);
        Assert.Equal("private target", await File.ReadAllTextAsync(targetFile, TestContext.Current.CancellationToken));
    }

    private LocalProfilePictureStorageService CreateStorage(string path = "pictures")
    {
        Directory.CreateDirectory(root);
        var environment = new StorageEnvironment { ContentRootPath = root, WebRootPath = Path.Combine(root, "wwwroot") };
        return new LocalProfilePictureStorageService(Options.Create(new ProfilePictureStorageOptions
        {
            ProfilePicturesPath = path
        }), environment, NullLogger<LocalProfilePictureStorageService>.Instance);
    }

    private static void CreateFileLinkOrSkip(string path, string target)
    {
        try { File.CreateSymbolicLink(path, target); }
        catch (PlatformNotSupportedException) { Assert.Skip("This filesystem does not support symbolic links."); }
        catch (Exception exception) when (OperatingSystem.IsWindows() && exception is UnauthorizedAccessException or IOException)
        {
            Assert.Skip("Windows symbolic-link permission is unavailable on this test host.");
        }
    }

    private static void CreateDirectoryLinkOrSkip(string path, string target)
    {
        try { Directory.CreateSymbolicLink(path, target); }
        catch (PlatformNotSupportedException) { Assert.Skip("This filesystem does not support symbolic links."); }
        catch (Exception exception) when (OperatingSystem.IsWindows() && exception is UnauthorizedAccessException or IOException)
        {
            Assert.Skip("Windows symbolic-link permission is unavailable on this test host.");
        }
    }

    public void Dispose()
    {
        // Only this fixture's unique directory beneath the system temp directory is eligible for removal.
        var prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(root);
        if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(target).StartsWith("fitness-storage-security-", StringComparison.Ordinal) && Directory.Exists(target))
            Directory.Delete(target, recursive: true);
    }

    private sealed class StorageEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "StorageSecurityTests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Testing";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
    }

    private sealed class LengthLyingFile(byte[] bytes) : IFormFile
    {
        public string ContentType => "image/png";
        public string ContentDisposition => "form-data; name=file; filename=portrait.png";
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => 1;
        public string Name => "file";
        public string FileName => "portrait.png";
        public Stream OpenReadStream() => new MemoryStream(bytes, writable: false);
        public void CopyTo(Stream target) => target.Write(bytes);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) =>
            target.WriteAsync(bytes, cancellationToken).AsTask();
    }
}
