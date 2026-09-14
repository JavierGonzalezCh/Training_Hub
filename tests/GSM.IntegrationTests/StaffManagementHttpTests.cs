using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Hueco H1: el owner da de alta, lista, desactiva y reactiva a su staff por <c>api/gym/staff</c>.
/// Con identidad real (JWT), porque la política y el aislamiento por gym dependen del token.
/// </summary>
public sealed class StaffManagementHttpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    [Fact]
    public async Task Owner_creates_staff_who_sets_password_with_the_emailed_code_and_logs_in()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var staffEmail = $"recep-{Guid.NewGuid():N}"[..14] + "@test.local";

        var created = await owner.PostAsJsonAsync("/api/gym/staff", new CreateStaffRequest(staffEmail, "Rita Recepción"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var staff = await created.Content.ReadFromJsonAsync<StaffResponse>();
        Assert.True(staff!.IsActive);

        // Sin contraseña todavía: no puede entrar.
        var early = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(staffEmail, "Whatever_12345"));
        Assert.Equal(HttpStatusCode.Unauthorized, early.StatusCode);

        var token = emails.LastTo(staffEmail)!.TextBody!.Split("código: ")[1].Trim();
        var reset = await host.CreateClient().PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(staffEmail, token, "StaffPass_12345"));
        Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());

        await LoggedInAsync(host, staffEmail, "StaffPass_12345");

        var list = await owner.GetFromJsonAsync<List<StaffResponse>>("/api/gym/staff");
        Assert.Contains(list!, s => s.Id == staff.Id);
    }

    [Fact]
    public async Task Duplicate_email_gets_409()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);

        var again = await owner.PostAsJsonAsync("/api/gym/staff", new CreateStaffRequest(w.StaffEmail, "Otra"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Deactivated_staff_cannot_log_in_or_refresh_until_reactivated()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);

        var login = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(w.StaffEmail, Password));
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokensResponse>();

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/gym/staff/{w.StaffId}")).StatusCode);

        var blocked = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(w.StaffEmail, Password));
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        var refresh = await host.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokens!.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        var reactivated = await owner.PostAsync($"/api/gym/staff/{w.StaffId}/reactivate", null);
        Assert.True((await reactivated.Content.ReadFromJsonAsync<StaffResponse>())!.IsActive);
        await LoggedInAsync(host, w.StaffEmail, Password);
    }

    [Fact]
    public async Task Staff_cannot_manage_staff()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail, Password);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/gym/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsJsonAsync("/api/gym/staff", new CreateStaffRequest("x@test.local", "X"))).StatusCode);
    }

    [Fact]
    public async Task Owner_cannot_see_or_touch_staff_of_another_gym_nor_the_owner_account()
    {
        var mine = await SeedAsync();
        var other = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, mine.OwnerEmail, Password);

        var list = await owner.GetFromJsonAsync<List<StaffResponse>>("/api/gym/staff");
        Assert.DoesNotContain(list!, s => s.Id == other.StaffId);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/gym/staff/{other.StaffId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/gym/staff/{mine.OwnerId}")).StatusCode);
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email, string password)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();

        var tokens = await login.Content.ReadFromJsonAsync<AuthTokensResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Staff Gym", $"staff-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Staff Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Staff Uno", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(ownerId, ownerEmail, staffId, staffEmail);
    }

    private sealed record World(Guid OwnerId, string OwnerEmail, Guid StaffId, string StaffEmail);
}
