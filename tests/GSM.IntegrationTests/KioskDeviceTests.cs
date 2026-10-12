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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// I7 — cuenta de dispositivo del kiosco, de punta a punta con identidad real (patrón de
/// <see cref="SubscriptionGateHttpTests"/>): JWT de verdad y el <see cref="CurrentUser"/> de producción.
/// </summary>
public sealed class KioskDeviceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    [Fact]
    public async Task Only_the_owner_creates_lists_and_revokes_kiosks()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();

        var staff = await LoggedInAsync(host, w.StaffEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/gym/kiosks", new { name = "Entrada" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/gym/kiosks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"/api/gym/kiosks/{Guid.NewGuid()}")).StatusCode);

        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var created = await CreateKioskAsync(owner, "Entrada principal");
        Assert.Equal(8, created.PairingCode.Length);
        Assert.Equal("pending", created.Device.Status);

        var list = await owner.GetFromJsonAsync<List<KioskDeviceResponse>>("/api/gym/kiosks");
        Assert.Contains(list!, k => k.Id == created.Device.Id && k.Name == "Entrada principal");

        var revoked = await owner.DeleteAsync($"/api/gym/kiosks/{created.Device.Id}");
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal("revoked", (await revoked.Content.ReadFromJsonAsync<KioskDeviceResponse>())!.Status);
    }

    [Fact]
    public async Task Kiosk_pairs_with_the_code_once_and_gets_a_kiosk_token_for_its_gym()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var created = await CreateKioskAsync(owner, "Entrada");

        var anonymous = host.CreateClient();
        var bad = await anonymous.PostAsJsonAsync("/api/auth/kiosk/pair", new { pairingCode = "ZZZZZZZZ" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        var paired = await anonymous.PostAsJsonAsync("/api/auth/kiosk/pair", new { pairingCode = created.PairingCode });
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var tokens = (await paired.Content.ReadFromJsonAsync<AuthTokensResponse>())!;

        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        Assert.Equal("kiosk", jwt.Claims.First(c => c.Type == "role").Value);
        Assert.Equal(w.GymId.ToString(), jwt.Claims.First(c => c.Type == "gym_id").Value);
        Assert.Equal(created.Device.Id.ToString(), jwt.Claims.First(c => c.Type == "sub").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "perm");

        // El código es de un solo uso.
        var again = await anonymous.PostAsJsonAsync("/api/auth/kiosk/pair", new { pairingCode = created.PairingCode });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);

        // La sesión se mantiene viva renovando con el token del dispositivo (no rota).
        var refreshed = await anonymous.PostAsJsonAsync("/api/auth/kiosk/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var next = (await refreshed.Content.ReadFromJsonAsync<AuthTokensResponse>())!;
        Assert.Equal(tokens.RefreshToken, next.RefreshToken);
        var again2 = await anonymous.PostAsJsonAsync("/api/auth/kiosk/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, again2.StatusCode);

        var list = await owner.GetFromJsonAsync<List<KioskDeviceResponse>>("/api/gym/kiosks");
        Assert.Equal("paired", list!.Single(k => k.Id == created.Device.Id).Status);
    }

    [Fact]
    public async Task An_expired_code_does_not_pair()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var created = await CreateKioskAsync(owner, "Entrada");

        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "update identity.kiosk_devices set pairing_code_expires_at = now() - interval '1 minute' where id = {0}",
                created.Device.Id);
        }

        var pair = await host.CreateClient().PostAsJsonAsync("/api/auth/kiosk/pair", new { pairingCode = created.PairingCode });
        Assert.Equal(HttpStatusCode.Unauthorized, pair.StatusCode);

        var list = await owner.GetFromJsonAsync<List<KioskDeviceResponse>>("/api/gym/kiosks");
        Assert.Equal("expired", list!.Single(k => k.Id == created.Device.Id).Status);
    }

    [Fact]
    public async Task Kiosk_token_gets_the_qr_code_and_nothing_else()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var kiosk = await PairedKioskAsync(host, w.OwnerEmail);

        var code = await kiosk.GetAsync("/api/attendance/kiosk-code");
        Assert.Equal(HttpStatusCode.OK, code.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await code.Content.ReadAsStringAsync()));

        foreach (var path in new[]
                 {
                     "/api/members", "/api/payments", "/api/gym", "/api/gym/staff", "/api/gym/kiosks",
                     "/api/attendance", "/api/attendance/settings", "/api/attendance/attempts", "/api/auth/me",
                     "/api/membership-plans", "/api/me/gym", "/api/me/achievements",
                 })
        {
            var response = await kiosk.GetAsync(path);
            Assert.True(
                response.StatusCode is HttpStatusCode.Forbidden,
                $"GET {path} debía dar 403 al kiosco y dio {(int)response.StatusCode}.");
        }

        foreach (var path in new[] { "/api/attendance/checkin", "/api/members", "/api/payments", "/api/gym/kiosks", "/api/attendance/kiosk-code/regenerate" })
        {
            var response = await kiosk.PostAsJsonAsync(path, new { });
            Assert.True(
                response.StatusCode is HttpStatusCode.Forbidden,
                $"POST {path} debía dar 403 al kiosco y dio {(int)response.StatusCode}.");
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await kiosk.PutAsJsonAsync("/api/attendance/settings", new { })).StatusCode);
    }

    [Fact]
    public async Task Revoking_the_kiosk_cuts_its_session()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var created = await CreateKioskAsync(owner, "Entrada");

        var anonymous = host.CreateClient();
        var tokens = (await (await anonymous.PostAsJsonAsync("/api/auth/kiosk/pair", new { pairingCode = created.PairingCode }))
            .Content.ReadFromJsonAsync<AuthTokensResponse>())!;

        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/kiosk/refresh", new { refreshToken = tokens.RefreshToken })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/gym/kiosks/{created.Device.Id}")).StatusCode);
        // Idempotente.
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/gym/kiosks/{created.Device.Id}")).StatusCode);

        // Sin sesión renovable, y el equipo vuelve a pedir código.
        var refresh = await anonymous.PostAsJsonAsync("/api/auth/kiosk/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Revoking_a_pending_kiosk_invalidates_its_code()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var created = await CreateKioskAsync(owner, "Entrada");

        await owner.DeleteAsync($"/api/gym/kiosks/{created.Device.Id}");

        var pair = await host.CreateClient().PostAsJsonAsync("/api/auth/kiosk/pair", new { pairingCode = created.PairingCode });
        Assert.Equal(HttpStatusCode.Unauthorized, pair.StatusCode);
    }

    [Fact]
    public async Task An_owner_cannot_revoke_a_kiosk_of_another_gym()
    {
        var a = await SeedAsync();
        var b = await SeedAsync(truncate: false);
        await using var host = RealIdentityHost();

        var ownerA = await LoggedInAsync(host, a.OwnerEmail);
        var kioskOfA = await CreateKioskAsync(ownerA, "De A");

        var ownerB = await LoggedInAsync(host, b.OwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.DeleteAsync($"/api/gym/kiosks/{kioskOfA.Device.Id}")).StatusCode);
        Assert.DoesNotContain(
            (await ownerB.GetFromJsonAsync<List<KioskDeviceResponse>>("/api/gym/kiosks"))!, k => k.Id == kioskOfA.Device.Id);
    }

    private static async Task<KioskCreatedResponse> CreateKioskAsync(HttpClient owner, string name)
    {
        var response = await owner.PostAsJsonAsync("/api/gym/kiosks", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<KioskCreatedResponse>())!;
    }

    private static async Task<HttpClient> PairedKioskAsync(WebApplicationFactory<Program> host, string ownerEmail)
    {
        var owner = await LoggedInAsync(host, ownerEmail);
        var created = await CreateKioskAsync(owner, "Entrada");
        var client = host.CreateClient();
        var tokens = (await (await client.PostAsJsonAsync("/api/auth/kiosk/pair", new { pairingCode = created.PairingCode }))
            .Content.ReadFromJsonAsync<AuthTokensResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();

        var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.Tokens!.AccessToken);
        return client;
    }

    private async Task<World> SeedAsync(bool truncate = true)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        if (truncate)
        {
            await db.Database.ExecuteSqlRawAsync(
                "truncate table identity.users, identity.kiosk_devices, gyms.gyms, gyms.gym_feature_flags restart identity cascade;");
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Kiosk Gym", $"kiosk-{suffix}");
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Kiosk Owner", Password, UserRole.Owner, gym.Id);
        await provisioning.CreateGymUserAsync(staffEmail, "Kiosk Staff", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerEmail, staffEmail);
    }

    private sealed record World(Guid GymId, string OwnerEmail, string StaffEmail);
}
