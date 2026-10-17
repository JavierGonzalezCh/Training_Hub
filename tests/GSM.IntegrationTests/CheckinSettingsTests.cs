using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.Application.Attendance;
using GSM.Application.Identity;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Xunit;
using GSM.Application.Common.Abstractions;

namespace GSM.IntegrationTests;

public sealed class CheckinSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_gym_without_settings_gets_the_defaults_and_nothing_is_persisted()
    {
        var gymId = await SeedGymAsync();

        using (var scope = Owner(gymId))
        {
            var settings = await scope.ServiceProvider.GetRequiredService<GetCheckinSettingsHandler>()
                .HandleAsync(default);
            Assert.Equal("Rotating", settings.Mode);
        }

        using var system = System();
        Assert.Equal(0, await system.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<CheckinSettings>().CountAsync(s => s.GymId == gymId));
    }

    [Fact]
    public async Task Update_persists_the_mode_and_rejects_an_invalid_one_in_the_domain()
    {
        var gymId = await SeedGymAsync();

        using (var scope = Owner(gymId))
        {
            var updated = await scope.ServiceProvider.GetRequiredService<UpdateCheckinSettingsHandler>()
                .HandleAsync(new UpdateCheckinSettingsRequest("Static"), default);
            Assert.Equal("Static", updated.Mode);
        }

        using (var scope = Owner(gymId))
        {
            var read = await scope.ServiceProvider.GetRequiredService<GetCheckinSettingsHandler>().HandleAsync(default);
            Assert.Equal("Static", read.Mode);
        }

        // La red de seguridad del dominio (el validador de FluentValidation corta antes por HTTP).
        using (var scope = Owner(gymId))
        {
            await Assert.ThrowsAsync<ValidationDomainException>(() => scope.ServiceProvider
                .GetRequiredService<UpdateCheckinSettingsHandler>()
                .HandleAsync(new UpdateCheckinSettingsRequest("Nope"), default));
        }
    }

    [Fact]
    public async Task Rotating_code_is_stable_within_a_window_and_changes_after_it()
    {
        var gymId = await SeedGymAsync();
        var start = new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

        string first, sameWindow, later;
        DateTimeOffset? expires;
        using (var scope = Owner(gymId, start.AddSeconds(5)))
        {
            var r = await scope.ServiceProvider.GetRequiredService<GetKioskCodeHandler>().HandleAsync(default);
            first = r.Code;
            expires = r.ExpiresAtUtc;
        }

        using (var scope = Owner(gymId, start.AddSeconds(20)))
        {
            sameWindow = (await scope.ServiceProvider.GetRequiredService<GetKioskCodeHandler>().HandleAsync(default)).Code;
        }

        using (var scope = Owner(gymId, start.AddSeconds(65)))
        {
            later = (await scope.ServiceProvider.GetRequiredService<GetKioskCodeHandler>().HandleAsync(default)).Code;
        }

        Assert.Equal(first, sameWindow);
        Assert.NotEqual(first, later);
        Assert.Equal(start.AddSeconds(30), expires);

        // Y el firmador acepta la ventana anterior pero no la de hace dos.
        using var s = Owner(gymId);
        var signer = s.ServiceProvider.GetRequiredService<GSM.Application.Attendance.Abstractions.ICheckinCodeSigner>();
        Assert.True(signer.Validate(gymId, first, CheckinMode.Rotating, 1, start.AddSeconds(35)));
        Assert.False(signer.Validate(gymId, first, CheckinMode.Rotating, 1, start.AddSeconds(65)));
        Assert.False(signer.Validate(Guid.NewGuid(), first, CheckinMode.Rotating, 1, start.AddSeconds(5)));
    }

    [Fact]
    public async Task Regenerate_changes_the_static_code_and_is_a_conflict_in_rotating_mode()
    {
        var gymId = await SeedGymAsync();

        using (var scope = Owner(gymId))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<RegenerateStaticCodeHandler>().HandleAsync(default));
            await scope.ServiceProvider.GetRequiredService<UpdateCheckinSettingsHandler>()
                .HandleAsync(new UpdateCheckinSettingsRequest("Static"), default);
        }

        string before, after;
        using (var scope = Owner(gymId))
        {
            before = (await scope.ServiceProvider.GetRequiredService<GetKioskCodeHandler>().HandleAsync(default)).Code;
            var regenerated = await scope.ServiceProvider.GetRequiredService<RegenerateStaticCodeHandler>().HandleAsync(default);
            after = regenerated.Code;
            Assert.Null(regenerated.ExpiresAtUtc);
        }

        Assert.NotEqual(before, after);
        using var check = Owner(gymId);
        Assert.Equal(after, (await check.ServiceProvider.GetRequiredService<GetKioskCodeHandler>().HandleAsync(default)).Code);
    }

    [Fact]
    public async Task Static_code_for_the_poster_is_served_only_in_static_mode_and_matches_the_kiosk_code()
    {
        var gymId = await SeedGymAsync();

        using (var scope = Owner(gymId))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<GetStaticCodeHandler>().HandleAsync(default));
            await scope.ServiceProvider.GetRequiredService<UpdateCheckinSettingsHandler>()
                .HandleAsync(new UpdateCheckinSettingsRequest("Static"), default);
        }

        using var check = Owner(gymId);
        var poster = await check.ServiceProvider.GetRequiredService<GetStaticCodeHandler>().HandleAsync(default);
        var kiosk = await check.ServiceProvider.GetRequiredService<GetKioskCodeHandler>().HandleAsync(default);
        Assert.Equal("Static", poster.Mode);
        Assert.Equal(kiosk.Code, poster.Code);
        Assert.Null(poster.ExpiresAtUtc);
    }

    [Fact]
    public async Task Kiosk_code_is_forbidden_when_checkin_qr_is_off_and_codes_are_per_gym()
    {
        var gymId = await SeedGymAsync(checkinQr: false);
        var otherGym = await SeedGymAsync();

        using (var scope = Owner(gymId))
        {
            await Assert.ThrowsAsync<ForbiddenDomainException>(() => scope.ServiceProvider
                .GetRequiredService<GetKioskCodeHandler>().HandleAsync(default));
        }

        var now = new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);
        string codeOther;
        using (var scope = Owner(otherGym, now))
        {
            codeOther = (await scope.ServiceProvider.GetRequiredService<GetKioskCodeHandler>().HandleAsync(default)).Code;
        }

        using var s = Owner(gymId);
        var signer = s.ServiceProvider.GetRequiredService<GSM.Application.Attendance.Abstractions.ICheckinCodeSigner>();
        Assert.False(signer.Validate(gymId, codeOther, CheckinMode.Rotating, 1, now));
    }

    [Fact]
    public async Task Kiosk_code_with_qr_off_but_document_on_is_served_without_the_code()
    {
        var gymId = await SeedGymAsync(checkinQr: false);

        using var scope = Owner(gymId);
        await scope.ServiceProvider.GetRequiredService<UpdateCheckinSettingsHandler>()
            .HandleAsync(new UpdateCheckinSettingsRequest("Rotating", ByDocument: true), default);

        var r = await scope.ServiceProvider.GetRequiredService<GetKioskCodeHandler>().HandleAsync(default);
        Assert.True(r.QrDisabled);
        Assert.True(r.ByDocument);
        Assert.Equal(string.Empty, r.Code);
        Assert.Null(r.ExpiresAtUtc);
    }

    [Fact]
    public async Task Http_validates_the_mode_and_only_the_owner_can_update()
    {
        var gymId = await SeedGymAsync();
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, GSM.API.Auth.CurrentUser>();
        }));

        HttpClient ClientFor(UserRole role)
        {
            var token = host.Services.GetRequiredService<IJwtTokenService>().CreateAccessToken(
                new AuthenticatedUser(Guid.CreateVersion7(), "u@test.local", "U", role, gymId));
            var client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            return client;
        }

        var owner = ClientFor(UserRole.Owner);
        var tooMuch = await owner.PutAsJsonAsync("/api/attendance/settings", new { mode = "Nope" });
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);

        var ok = await owner.PutAsJsonAsync("/api/attendance/settings", new { mode = "Static" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var staff = ClientFor(UserRole.Staff);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/attendance/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PutAsJsonAsync("/api/attendance/settings", new { mode = "Rotating" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync("/api/attendance/kiosk-code/regenerate", null)).StatusCode);
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Owner(Guid gymId, DateTimeOffset? utcNow = null)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Owner);
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        if (utcNow is { } now)
        {
            clock.PinTo(now, DateOnly.FromDateTime(now.UtcDateTime));
        }
        else
        {
            clock.Unpin();
        }

        return scope;
    }

    private async Task<Guid> SeedGymAsync(bool checkinQr = true)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("Checkin Gym", $"ck-{Guid.NewGuid():N}"[..16]);
        var flags = GymFeatureFlags.Default(gym.Id);
        if (!checkinQr)
        {
            flags.Update(true, true, true, true, true, false, false);
        }

        db.AddRange(gym, flags);
        await db.SaveChangesAsync();
        return gym.Id;
    }
}
