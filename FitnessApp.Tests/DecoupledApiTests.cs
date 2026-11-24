using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitnessApp.Models;

namespace FitnessApp.Tests;

public class DecoupledApiTests
{
    [Fact]
    public async Task Frontend_purchase_checkin_edit_and_cancel_preserve_allowance_and_ownership()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new FitnessAppFactory { UseLegacyMvc = false };
        using var admin = await factory.RegisterAsync(UserRoles.Admin);
        using var member = await factory.RegisterAsync();
        using var other = await factory.RegisterAsync();
        await Csrf(admin.Client);
        await Csrf(member.Client);
        await Csrf(other.Client);
        async Task<int> CreatedId(HttpResponseMessage response)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                return data.RootElement.GetProperty("id").GetInt32();
            }
        }
        var typeId = await CreatedId(await admin.Client.PostAsJsonAsync("/api/passes", new { name = "Workflow pass", price = 20, maxVisits = 3, validityDays = 30 }, ct));
        var trainerId = await CreatedId(await admin.Client.PostAsJsonAsync("/api/trainers", new { fullName = "Workflow trainer", specialty = "Strength", phone = "123456789" }, ct));
        var passId = await CreatedId(await admin.Client.PostAsJsonAsync($"/api/customers/{member.Id}/passes", new { passTypeId = typeId }, ct));
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync("/api/visits", new { customerPassId = passId }, ct)).StatusCode);
        var visitId = await CreatedId(await member.Client.PostAsJsonAsync("/api/visits", new { customerPassId = passId }, ct));
        Assert.Equal(HttpStatusCode.Conflict, (await member.Client.PostAsJsonAsync("/api/visits", new { customerPassId = passId }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PutAsJsonAsync($"/api/visits/{visitId}", new { trainerId }, ct)).StatusCode);
        using (var response = await member.Client.GetAsync("/api/members/me", ct))
        {
            response.EnsureSuccessStatusCode();
            using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            Assert.Equal(2, data.RootElement.GetProperty("passes")[0].GetProperty("remainingVisits").GetInt32());
        }
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.DeleteAsync($"/api/passes/{typeId}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.DeleteAsync($"/api/trainers/{trainerId}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/visits/{visitId}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.DeleteAsync($"/api/visits/{visitId}", ct)).StatusCode);
        using var final = await member.Client.GetAsync("/api/members/me", ct);
        using var finalData = JsonDocument.Parse(await final.Content.ReadAsStringAsync(ct));
        Assert.Equal(3, finalData.RootElement.GetProperty("passes")[0].GetProperty("remainingVisits").GetInt32());
    }

    private static async Task Csrf(HttpClient client)
    {
        using var response = await client.GetAsync("/api/session/antiforgery");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", json.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Catalog_requires_admin_and_antiforgery_and_supports_crud()
    {
        await using var factory = new FitnessAppFactory { UseLegacyMvc = false };
        using var anonymous = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/passes")).StatusCode);
        using var member = await factory.RegisterAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.GetAsync("/api/passes")).StatusCode);
        using var admin = await factory.RegisterAsync(UserRoles.Admin);
        admin.Client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        var payload = new { name = "API membership", price = 20, maxVisits = 10, validityDays = 30 };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PostAsJsonAsync("/api/passes", payload)).StatusCode);
        await Csrf(admin.Client);
        var created = await admin.Client.PostAsJsonAsync("/api/passes", payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync($"/api/passes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PutAsJsonAsync($"/api/passes/{id}", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsJsonAsync("/api/passes", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/passes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.GetAsync($"/api/passes/{id}")).StatusCode);
    }

    [Fact]
    public async Task Session_login_requires_antiforgery_and_sets_private_cookies()
    {
        await using var factory = new FitnessAppFactory { UseLegacyMvc = false };
        using var account = await factory.RegisterAsync();
        using var client = factory.NewClient();
        var credentials = new { email = account.Email, password = FitnessAppFactory.Password };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/session", credentials)).StatusCode);
        await Csrf(client);
        var login = await client.PostAsJsonAsync("/api/session", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("access_token=") && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        await Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/session/refresh", null)).StatusCode);
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task Api_only_host_exposes_all_read_models_and_cors_without_mvc()
    {
        await using var factory = new FitnessAppFactory { UseLegacyMvc = false };
        using var admin = await factory.RegisterAsync(UserRoles.Admin);
        foreach (var path in new[] { "/api/dashboard", "/api/customers", "/api/visits", "/api/visits/options", "/api/visits/trainers", "/api/trainers", "/api/accounts", "/api/members/me", "/api/statistics/Data" })
        {
            using var response = await admin.Client.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.GetAsync("/Home/Index", TestContext.Current.CancellationToken)).StatusCode);
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/passes");
        preflight.Headers.Add("Origin", "http://localhost:5173");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,requestverificationtoken");
        using var cors = await admin.Client.SendAsync(preflight, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, cors.StatusCode);
        Assert.Equal("http://localhost:5173", cors.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", cors.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }
}

