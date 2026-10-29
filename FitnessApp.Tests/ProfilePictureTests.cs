using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FitnessApp.Models;
using FitnessApp.Data;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace FitnessApp.Tests;

public sealed class ProfilePictureTests(FitnessAppFactory app) : IClassFixture<FitnessAppFactory>
{
    private const string Endpoint = "/api/users/me/profile-picture";

    [Theory]
    [InlineData("jpg", "image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("jpeg", "image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("png", "image/png", SKEncodedImageFormat.Png)]
    [InlineData("webp", "image/webp", SKEncodedImageFormat.Webp)]
    public async Task Uploads_supported_images_as_resized_webp(string extension, string mime, SKEncodedImageFormat format)
    {
        using var account = await app.RegisterAsync();
        var response = await UploadAsync(account.Client, ImageBytes(format, 1024, 256), "portrait." + extension, mime);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = await PictureUrlAsync(response);
        Assert.StartsWith($"/api/users/{account.Id}/profile-picture", url);
        Assert.DoesNotContain(app.Root, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var picture = await account.Client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("image/webp", picture.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", picture.Headers.CacheControl?.ToString() ?? "");
        Assert.Equal("nosniff", picture.Headers.GetValues("X-Content-Type-Options").Single());
        using var decoded = SKBitmap.Decode(await picture.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(512, decoded.Width);
        Assert.Equal(128, decoded.Height);
        var key = await app.PictureKeyAsync(account.Id);
        Assert.Matches("^[a-f0-9]{32}\\.webp$", key!);
        Assert.True(File.Exists(Path.Combine(app.Pictures, key!)));
        var dto = await account.Client.GetFromJsonAsync<JsonElement>("/api/users/me", TestContext.Current.CancellationToken);
        Assert.StartsWith($"/api/users/{account.Id}/profile-picture", dto.GetProperty("profilePictureUrl").GetString());
    }

    [Fact]
    public async Task Small_images_are_not_upscaled()
    {
        using var account = await app.RegisterAsync();
        var upload = await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png, 40, 20));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var decoded = SKBitmap.Decode(await account.Client.GetByteArrayAsync(await PictureUrlAsync(upload), TestContext.Current.CancellationToken));
        Assert.Equal(40, decoded.Width);
        Assert.Equal(20, decoded.Height);
    }

    [Theory]
    [InlineData("../../outside.png")]
    [InlineData("C:\\private\\outside.png")]
    public async Task Client_filenames_never_control_the_storage_location(string filename)
    {
        using var account = await app.RegisterAsync();
        var upload = await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png), filename);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var key = await app.PictureKeyAsync(account.Id);
        Assert.Matches("^[a-f0-9]{32}\\.webp$", key!);
        Assert.True(File.Exists(Path.Combine(app.Pictures, key!)));
    }

    [Theory]
    [InlineData(1, 0, 1, 2, 3)]
    [InlineData(2, 1, 0, 3, 2)]
    [InlineData(3, 3, 2, 1, 0)]
    [InlineData(4, 2, 3, 0, 1)]
    [InlineData(5, 0, 2, 1, 3)]
    [InlineData(6, 2, 0, 3, 1)]
    [InlineData(7, 3, 1, 2, 0)]
    [InlineData(8, 1, 3, 0, 2)]
    public async Task Corrects_all_exif_orientations_and_discards_metadata(int orientation, int tl, int tr, int bl, int br)
    {
        using var account = await app.RegisterAsync();
        var jpeg = OrientedJpeg(orientation);
        var upload = await UploadAsync(account.Client, jpeg, "camera.jpg", "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var bytes = await account.Client.GetByteArrayAsync(await PictureUrlAsync(upload), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("EXIF", Encoding.ASCII.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(orientation >= 5 ? 40 : 80, bitmap.Width);
        Assert.Equal(orientation >= 5 ? 80 : 40, bitmap.Height);
        AssertColor(Palette[tl], bitmap.GetPixel(bitmap.Width / 4, bitmap.Height / 4));
        AssertColor(Palette[tr], bitmap.GetPixel(3 * bitmap.Width / 4, bitmap.Height / 4));
        AssertColor(Palette[bl], bitmap.GetPixel(bitmap.Width / 4, 3 * bitmap.Height / 4));
        AssertColor(Palette[br], bitmap.GetPixel(3 * bitmap.Width / 4, 3 * bitmap.Height / 4));
    }

    [Theory]
    [InlineData("portrait.gif", "image/gif")]
    [InlineData("portrait.svg", "image/svg+xml")]
    [InlineData("portrait.exe", "image/png")]
    [InlineData("portrait.png", "text/plain")]
    [InlineData("portrait.jpg", "image/png")]
    [InlineData("portrait.webp", "image/webp")]
    public async Task Rejects_unsupported_extensions_mime_and_format_mismatches(string filename, string mime)
    {
        using var account = await app.RegisterAsync();
        var response = await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png), filename, mime);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Null(await app.PictureKeyAsync(account.Id));
    }

    [Fact]
    public async Task Rejects_an_unsupported_decoded_format_even_with_allowed_name_and_mime()
    {
        using var account = await app.RegisterAsync();
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
        var response = await UploadAsync(account.Client, gif, "spoof.png", "image/png");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_empty_and_corrupted_images()
    {
        using var account = await app.RegisterAsync();
        foreach (var bytes in new[] { Array.Empty<byte>(), Encoding.UTF8.GetBytes("This is not an image.") })
        {
            var response = await UploadAsync(account.Client, bytes);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var valid = ImageBytes(SKEncodedImageFormat.Png, 512, 512);
        var truncated = await UploadAsync(account.Client, valid[..(valid.Length / 2)]);
        Assert.Equal(HttpStatusCode.BadRequest, truncated.StatusCode);
        Assert.Null(await app.PictureKeyAsync(account.Id));
    }

    [Fact]
    public async Task Rejects_files_over_five_megabytes()
    {
        using var account = await app.RegisterAsync();
        var response = await UploadAsync(account.Client, new byte[5 * 1024 * 1024 + 1]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Null(await app.PictureKeyAsync(account.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejects_grossly_oversized_multipart_requests_with_413(bool includeContentLength)
    {
        using var account = await app.RegisterAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        var form = Form(new byte[6 * 1024 * 1024]);
        request.Content = includeContentLength ? form : new StreamingMultipartContent(form);
        request.Headers.Add("RequestVerificationToken", await app.AntiforgeryAsync(account.Client));
        if (!includeContentLength) request.Headers.TransferEncodingChunked = true;
        var response = await account.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Null(await app.PictureKeyAsync(account.Id));
    }

    [Fact]
    public async Task Malformed_multipart_requests_return_safe_400()
    {
        using var account = await app.RegisterAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("RequestVerificationToken", await app.AntiforgeryAsync(account.Client));
        request.Content = new StringContent("--broken\r\nContent-Disposition: form-data; name=\"file\"; filename=\"picture.png\"\r\n", Encoding.UTF8);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=broken");
        var response = await account.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(app.Root, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Null(await app.PictureKeyAsync(account.Id));
    }

    [Fact]
    public async Task Rejects_excessive_image_dimensions_before_full_decode()
    {
        using var account = await app.RegisterAsync();
        var response = await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png, 8193, 1));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await app.PictureKeyAsync(account.Id));
    }

    [Fact]
    public async Task Rejects_excessive_decoded_pixel_count()
    {
        using var account = await app.RegisterAsync();
        var response = await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png, 4001, 4000));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await app.PictureKeyAsync(account.Id));
    }

    [Fact]
    public async Task Anonymous_upload_delete_and_retrieval_return_401()
    {
        using var client = app.NewClient();
        using var form = Form(ImageBytes(SKEncodedImageFormat.Png));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(Endpoint, form, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync(Endpoint, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/1/profile-picture", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Cookie_mutations_require_valid_antiforgery_tokens()
    {
        using var account = await app.RegisterAsync();
        using var form = Form(ImageBytes(SKEncodedImageFormat.Png));
        Assert.Equal(HttpStatusCode.BadRequest, (await account.Client.PostAsync(Endpoint, form, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await account.Client.DeleteAsync(Endpoint, TestContext.Current.CancellationToken)).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = Form(ImageBytes(SKEncodedImageFormat.Png)) };
        request.Headers.Add("RequestVerificationToken", "invalid-token");
        Assert.Equal(HttpStatusCode.BadRequest, (await account.Client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Null(await app.PictureKeyAsync(account.Id));
    }

    [Fact]
    public async Task Bearer_access_token_supports_mutations_without_cookie_antiforgery()
    {
        using var account = await app.RegisterAsync();
        using var client = await app.BearerClientAsync(account.Id);
        using var form = Form(ImageBytes(SKEncodedImageFormat.Png));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(Endpoint, form, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Endpoint, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Adding_a_bearer_header_cannot_bypass_cookie_antiforgery()
    {
        using var account = await app.RegisterAsync();
        using var bearer = await app.BearerClientAsync(account.Id);
        account.Client.DefaultRequestHeaders.Authorization = bearer.DefaultRequestHeaders.Authorization;
        using var form = Form(ImageBytes(SKEncodedImageFormat.Png));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await account.Client.PostAsync(Endpoint, form, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Null(await app.PictureKeyAsync(account.Id));
    }

    [Fact]
    public async Task Refresh_tokens_cannot_authenticate_picture_requests()
    {
        using var account = await app.RegisterAsync();
        var login = await account.Client.PostAsJsonAsync("/auth/login", new { email = account.Email, password = FitnessAppFactory.Password }, TestContext.Current.CancellationToken);
        var refresh = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("refresh_token="))
            .Split(';')[0]["refresh_token=".Length..];
        using var attacker = app.NewClient();
        attacker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Uri.UnescapeDataString(refresh));
        using var form = Form(ImageBytes(SKEncodedImageFormat.Png));
        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.PostAsync(Endpoint, form, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Members_cannot_view_other_pictures_and_admins_can_view()
    {
        using var owner = await app.RegisterAsync();
        using var other = await app.RegisterAsync();
        using var admin = await app.RegisterAsync(UserRoles.Admin);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(owner.Client, ImageBytes(SKEncodedImageFormat.Png))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/users/{owner.Id}/profile-picture", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync($"/api/users/{owner.Id}/profile-picture", TestContext.Current.CancellationToken)).StatusCode);
        // No endpoint accepts a target account ID for writes.
        using var form = Form(ImageBytes(SKEncodedImageFormat.Png));
        Assert.False((await other.Client.PostAsync($"/api/users/{owner.Id}/profile-picture", form, TestContext.Current.CancellationToken)).IsSuccessStatusCode);
        Assert.False((await other.Client.DeleteAsync($"/api/users/{owner.Id}/profile-picture", TestContext.Current.CancellationToken)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Overposted_user_id_does_not_change_another_account()
    {
        using var owner = await app.RegisterAsync();
        using var other = await app.RegisterAsync();
        using var form = Form(ImageBytes(SKEncodedImageFormat.Png));
        form.Add(new StringContent(owner.Id.ToString()), "userId");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
        request.Headers.Add("RequestVerificationToken", await app.AntiforgeryAsync(other.Client));
        Assert.Equal(HttpStatusCode.OK, (await other.Client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Null(await app.PictureKeyAsync(owner.Id));
        Assert.NotNull(await app.PictureKeyAsync(other.Id));
    }

    [Fact]
    public async Task Replacement_commits_new_picture_then_removes_old_file()
    {
        using var account = await app.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png))).StatusCode);
        var oldKey = await app.PictureKeyAsync(account.Id);
        var replacement = await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png, 30, 60));
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var newKey = await app.PictureKeyAsync(account.Id);
        Assert.NotEqual(oldKey, newKey);
        Assert.False(File.Exists(Path.Combine(app.Pictures, oldKey!)));
        Assert.True(File.Exists(Path.Combine(app.Pictures, newKey!)));
        using var decoded = SKBitmap.Decode(await account.Client.GetByteArrayAsync(await PictureUrlAsync(replacement), TestContext.Current.CancellationToken));
        Assert.Equal(30, decoded.Width);
        Assert.Equal(60, decoded.Height);
    }

    [Fact]
    public async Task Stale_concurrent_replacement_cannot_overwrite_the_winner_or_leave_an_unreferenced_file()
    {
        using var account = await app.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png))).StatusCode);
        using var firstScope = app.Services.CreateScope();
        using var staleScope = app.Services.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await firstDb.Users.SingleAsync(x => x.Id == account.Id, TestContext.Current.CancellationToken);
        await staleDb.Users.SingleAsync(x => x.Id == account.Id, TestContext.Current.CancellationToken);
        var firstService = firstScope.ServiceProvider.GetRequiredService<UserProfileService>();
        var staleService = staleScope.ServiceProvider.GetRequiredService<UserProfileService>();
        var bytes = ImageBytes(SKEncodedImageFormat.Png);
        using var input = new MemoryStream(bytes);
        var file = new FormFile(input, 0, bytes.Length, "file", "portrait.png")
        {
            Headers = new HeaderDictionary(), ContentType = "image/png"
        };
        var winner = await firstService.ReplaceAsync(account.Id, file, TestContext.Current.CancellationToken);
        var before = Directory.GetFiles(app.Pictures).Order().ToArray();
        input.Position = 0;
        var error = await Assert.ThrowsAsync<ProfilePictureException>(() =>
            staleService.ReplaceAsync(account.Id, file, TestContext.Current.CancellationToken));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(winner, await app.PictureKeyAsync(account.Id));
        Assert.Equal(before, Directory.GetFiles(app.Pictures).Order().ToArray());
        Assert.True(File.Exists(Path.Combine(app.Pictures, winner)));
    }

    [Fact]
    public async Task Removal_is_idempotent_and_restores_default_avatar()
    {
        using var account = await app.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png))).StatusCode);
        var key = await app.PictureKeyAsync(account.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(account.Client)).StatusCode);
        Assert.Null(await app.PictureKeyAsync(account.Id));
        Assert.False(File.Exists(Path.Combine(app.Pictures, key!)));
        var picture = await account.Client.GetAsync($"/api/users/{account.Id}/profile-picture", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("image/svg+xml", picture.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(account.Client)).StatusCode);
    }

    [Fact]
    public async Task Users_without_pictures_receive_default_avatar_and_nullable_profile_url()
    {
        using var account = await app.RegisterAsync();
        var dto = await account.Client.GetFromJsonAsync<JsonElement>("/api/users/me", TestContext.Current.CancellationToken);
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("profilePictureUrl").ValueKind);
        var picture = await account.Client.GetAsync($"/api/users/{account.Id}/profile-picture", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("image/svg+xml", picture.Content.Headers.ContentType?.MediaType);
        var page = await account.Client.GetStringAsync("/Member/Index", TestContext.Current.CancellationToken);
        Assert.Contains("data-profile-picture", page);
        Assert.Contains("Change Profile Picture", page);
        Assert.Contains("__RequestVerificationToken", page);
    }

    [Fact]
    public async Task Missing_saved_file_falls_back_without_exposing_a_path()
    {
        using var account = await app.RegisterAsync();
        await app.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(x => x.Id == account.Id, TestContext.Current.CancellationToken)).ProfilePicturePath = Guid.NewGuid().ToString("N") + ".webp";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        var picture = await account.Client.GetAsync($"/api/users/{account.Id}/profile-picture", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("image/svg+xml", picture.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(app.Root, await picture.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Traversal_in_persisted_key_cannot_read_or_delete_outside_storage()
    {
        using var account = await app.RegisterAsync();
        var outsideFile = Path.Combine(app.Root, "private.webp");
        await File.WriteAllTextAsync(outsideFile, "must stay private", TestContext.Current.CancellationToken);
        await app.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(x => x.Id == account.Id, TestContext.Current.CancellationToken)).ProfilePicturePath = "../private.webp";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        var response = await account.Client.GetAsync($"/api/users/{account.Id}/profile-picture", TestContext.Current.CancellationToken);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(account.Client)).StatusCode);
        Assert.Equal("must stay private", await File.ReadAllTextAsync(outsideFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Failed_database_replacement_keeps_old_picture_and_cleans_new_file()
    {
        using var account = await app.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png))).StatusCode);
        var key = await app.PictureKeyAsync(account.Id);
        var before = Directory.GetFiles(app.Pictures).Order().ToArray();
        app.Failures.FailNextDatabaseWrite = 1;
        var response = await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png, 33, 22));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(key, await app.PictureKeyAsync(account.Id));
        Assert.Equal(before, Directory.GetFiles(app.Pictures).Order().ToArray());
        Assert.Equal(HttpStatusCode.OK, (await account.Client.GetAsync($"/api/users/{account.Id}/profile-picture", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Failed_database_removal_keeps_reference_and_file()
    {
        using var account = await app.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png))).StatusCode);
        var key = await app.PictureKeyAsync(account.Id);
        app.Failures.FailNextDatabaseWrite = 1;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await RemoveAsync(account.Client)).StatusCode);
        Assert.Equal(key, await app.PictureKeyAsync(account.Id));
        Assert.True(File.Exists(Path.Combine(app.Pictures, key!)));
    }

    [Fact]
    public async Task Failed_storage_write_preserves_existing_reference()
    {
        using var account = await app.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png))).StatusCode);
        var key = await app.PictureKeyAsync(account.Id);
        app.Failures.FailNextSave = 1;
        var response = await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(key, await app.PictureKeyAsync(account.Id));
        Assert.True(File.Exists(Path.Combine(app.Pictures, key!)));
        Assert.DoesNotContain(app.Root, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Failed_cleanup_does_not_revert_a_committed_replacement()
    {
        using var account = await app.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png))).StatusCode);
        var key = await app.PictureKeyAsync(account.Id);
        app.Failures.FailNextDelete = 1;
        var response = await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png, 40, 60));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newKey = await app.PictureKeyAsync(account.Id);
        Assert.NotEqual(key, newKey);
        Assert.True(File.Exists(Path.Combine(app.Pictures, newKey!)));
        Assert.Equal(HttpStatusCode.OK, (await account.Client.GetAsync(await PictureUrlAsync(response), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Failed_storage_read_returns_safe_503()
    {
        using var account = await app.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png))).StatusCode);
        app.Failures.FailNextRead = 1;
        var response = await account.Client.GetAsync($"/api/users/{account.Id}/profile-picture", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(app.Root, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Picture_survives_storage_service_recreation()
    {
        using var account = await app.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(account.Client, ImageBytes(SKEncodedImageFormat.Png))).StatusCode);
        var key = await app.PictureKeyAsync(account.Id);
        using var scope = app.Services.CreateScope();
        var recreated = ActivatorUtilities.CreateInstance<FitnessApp.Service.LocalProfilePictureStorageService>(scope.ServiceProvider);
        await using var picture = await recreated.OpenReadAsync(key!, TestContext.Current.CancellationToken);
        Assert.NotNull(picture);
        Assert.True(picture.Length > 0);
    }

    [Fact]
    public async Task Existing_registration_login_refresh_and_logout_continue_to_work()
    {
        using var account = await app.RegisterAsync();
        var before = await account.Client.GetAsync("/api/users/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var login = await account.Client.PostAsJsonAsync("/auth/login", new { email = account.Email, password = FitnessAppFactory.Password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), x => x.StartsWith("access_token=") && x.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await account.Client.PostAsync("/auth/refresh", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await account.Client.GetAsync("/Member/Index", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await account.Client.DeleteAsync("/auth/session", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await account.Client.GetAsync("/api/users/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await account.Client.PostAsync("/auth/refresh", null, TestContext.Current.CancellationToken)).StatusCode);
    }

    private async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string filename = "portrait.png", string mime = "image/png")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = Form(bytes, filename, mime) };
        request.Headers.Add("RequestVerificationToken", await app.AntiforgeryAsync(client));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> RemoveAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, Endpoint);
        request.Headers.Add("RequestVerificationToken", await app.AntiforgeryAsync(client));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static MultipartFormDataContent Form(byte[] bytes, string filename = "portrait.png", string mime = "image/png")
    {
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(bytes);
        image.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(image, "file", filename);
        return form;
    }

    private static async Task<string> PictureUrlAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("profilePictureUrl").GetString()!;
    }

    private static byte[] ImageBytes(SKEncodedImageFormat format, int width = 80, int height = 40)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }

    private static readonly SKColor[] Palette = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow];

    private static byte[] OrientedJpeg(int orientation)
    {
        using var bitmap = new SKBitmap(80, 40);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, Palette[(y >= 20 ? 2 : 0) + (x >= 40 ? 1 : 0)]);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        var jpeg = encoded.ToArray();
        byte[] exif = [0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];
        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }

    private static void AssertColor(SKColor expected, SKColor actual)
    {
        Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, 60);
        Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, 60);
        Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, 60);
    }

    private sealed class StreamingMultipartContent : HttpContent
    {
        private readonly HttpContent inner;

        public StreamingMultipartContent(HttpContent inner)
        {
            this.inner = inner;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            inner.CopyToAsync(stream, TestContext.Current.CancellationToken);
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
