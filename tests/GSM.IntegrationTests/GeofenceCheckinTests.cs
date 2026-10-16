using GSM.Application.Attendance;
using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Public;
using GSM.Application.Gyms.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// A6: geolocalización con el QR fijo. Solo el resultado (<c>location_check</c>) se guarda; fuera del radio o sin
/// ubicación la entrada se registra pero no cuenta para logros ni racha.
/// </summary>
public sealed class GeofenceCheckinTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    // Bogotá, plaza de Bolívar. 0.001 grados de latitud son ~111 m.
    private const double GymLat = 4.5981;
    private const double GymLon = -74.0760;

    [Theory]
    [InlineData(0.0, 0.0, "Inside")]       // en la puerta
    [InlineData(0.001, 0.0, "Inside")]     // ~111 m, dentro de 150 m
    [InlineData(0.002, 0.0, "Outside")]    // ~222 m, fuera
    public async Task Static_code_with_coordinates_records_inside_or_outside(double dLat, double dLon, string expected)
    {
        var w = await SeedAsync(coordinates: true, mode: "Static");

        var (res, _) = await CheckInAsMemberAsync(w, GymLat + dLat, GymLon + dLon);

        Assert.Equal(expected, res.LocationCheck);
        Assert.Equal(expected == "Inside", res.CountsForProgress);
        Assert.Equal(expected == "Inside", res.LocationNotice is null);
    }

    [Fact]
    public async Task Sharing_no_location_is_denied_and_the_entry_still_registers()
    {
        var w = await SeedAsync(coordinates: true, mode: "Static");

        var (res, created) = await CheckInAsMemberAsync(w, null, null);

        Assert.True(created);
        Assert.Equal("Denied", res.LocationCheck);
        Assert.False(res.CountsForProgress);
        Assert.Contains("sin tu ubicación", res.LocationNotice);
        Assert.Empty(res.UnlockedAchievements);
    }

    [Fact]
    public async Task Outside_the_radius_does_not_count_for_streak_visits_or_achievements_but_does_for_occupancy()
    {
        var w = await SeedAsync(coordinates: true, mode: "Static");
        await CheckInAsMemberAsync(w, GymLat + 0.01, GymLon);

        using var s = Scope(w, UserRole.Staff);
        var reader = s.ServiceProvider.GetRequiredService<IAttendanceReader>();
        Assert.Equal(0, await reader.GetVisitCountAsync(w.MemberId));
        Assert.Equal(0, await reader.GetStreakAsync(w.MemberId));
        Assert.Empty(await reader.GetAttendanceDatesAsync(w.MemberId));
        Assert.Equal(1, await reader.GetTodayCountAsync(w.GymId));

        var row = await s.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<GSM.Domain.Attendance.Attendance>().SingleAsync(a => a.MemberId == w.MemberId);
        Assert.Equal(LocationCheck.Outside, row.LocationCheck);
    }

    [Fact]
    public async Task Inside_counts_like_any_other_entry()
    {
        var w = await SeedAsync(coordinates: true, mode: "Static");
        await CheckInAsMemberAsync(w, GymLat, GymLon);

        using var s = Scope(w, UserRole.Staff);
        var reader = s.ServiceProvider.GetRequiredService<IAttendanceReader>();
        Assert.Equal(1, await reader.GetVisitCountAsync(w.MemberId));
        Assert.Equal(1, await reader.GetStreakAsync(w.MemberId));
    }

    [Fact]
    public async Task Rotating_code_never_asks_for_location_and_always_counts()
    {
        var w = await SeedAsync(coordinates: true, mode: "Rotating");

        var (res, _) = await CheckInAsMemberAsync(w, null, null);

        Assert.Null(res.LocationCheck);
        Assert.True(res.CountsForProgress);
    }

    [Fact]
    public async Task A_gym_without_coordinates_never_asks_and_always_counts()
    {
        var w = await SeedAsync(coordinates: false, mode: "Static");

        var (res, _) = await CheckInAsMemberAsync(w, null, null);

        Assert.Null(res.LocationCheck);
        Assert.True(res.CountsForProgress);
    }

    [Fact]
    public async Task Staff_manual_entry_is_never_geofenced()
    {
        var w = await SeedAsync(coordinates: true, mode: "Static");

        using var s = Scope(w, UserRole.Staff);
        var (res, _) = await s.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(w.MemberId, null), default);

        Assert.Null(res.LocationCheck);
        Assert.True(res.CountsForProgress);
    }

    [Fact]
    public async Task Invalid_coordinates_are_treated_as_denied()
    {
        var w = await SeedAsync(coordinates: true, mode: "Static");

        var (res, _) = await CheckInAsMemberAsync(w, 999, 999);

        Assert.Equal("Denied", res.LocationCheck);
    }

    [Theory]
    [InlineData("Static", true, true)]
    [InlineData("Static", false, false)]
    [InlineData("Rotating", true, false)]
    public async Task Location_policy_is_required_only_with_the_static_code_and_coordinates(
        string mode, bool coordinates, bool expected)
    {
        var w = await SeedAsync(coordinates, mode);

        using var s = Scope(w, UserRole.Member);
        var policy = await s.ServiceProvider.GetRequiredService<GetCheckinLocationPolicyHandler>().HandleAsync(default);

        Assert.Equal(expected, policy.LocationRequired);
    }

    [Fact]
    public async Task Staff_can_filter_the_entries_that_did_not_count()
    {
        var w = await SeedAsync(coordinates: true, mode: "Static");
        await CheckInAsMemberAsync(w, GymLat + 0.01, GymLon);

        using var s = Scope(w, UserRole.Staff);
        var page = await s.ServiceProvider.GetRequiredService<ListAttendanceHandler>()
            .HandleAsync(new ListAttendanceQuery { NotCountedOnly = true }, default);

        var item = Assert.Single(page.Items);
        Assert.Equal("Outside", item.LocationCheck);
        Assert.False(item.CountsForProgress);
    }

    [Fact]
    public void Haversine_matches_a_known_distance()
    {
        // ~111.2 km por grado de latitud.
        var meters = GeoDistance.Meters(0, 0, 1, 0);
        Assert.InRange(meters, 111_000, 111_400);
        Assert.Equal(0, GeoDistance.Meters(GymLat, GymLon, GymLat, GymLon), 3);
    }

    // ---- helpers ----

    private IServiceScope Scope(World w, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(role == UserRole.Member ? w.MemberUserId : Guid.CreateVersion7(), w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<(CheckInResponse Response, bool Created)> CheckInAsMemberAsync(
        World w, double? latitude, double? longitude)
    {
        string code;
        using (var scope = factory.CreateScopeWithMigration())
        {
            var now = new GSM.Infrastructure.Common.SystemClock(
                    Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
                .BusinessDayRange(Today).Start.AddHours(12);
            code = scope.ServiceProvider.GetRequiredService<ICheckinCodeSigner>()
                .Generate(w.GymId, w.Mode, 1, now);
        }

        using var s = Scope(w, UserRole.Member);
        return await s.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(null, code, null, latitude, longitude), default);
    }

    private async Task<World> SeedAsync(bool coordinates, string mode)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Geofence Gym", $"gf-{Guid.NewGuid():N}"[..16]);
        if (coordinates)
        {
            gym.UpdateLocation("Calle 1", "Bogotá", GymLat, GymLon, 150);
        }

        var userId = Guid.CreateVersion7();
        var member = Member.Create(gym.Id, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-100));
        member.LinkUser(userId);
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), member, plan,
            Membership.Create(gym.Id, member.Id, plan.Id, Today.AddDays(-5), 30));
        await db.SaveChangesAsync();

        var checkinMode = Enum.Parse<CheckinMode>(mode);
        if (checkinMode == CheckinMode.Static)
        {
            var staticWorld = new World(gym.Id, member.Id, userId, checkinMode);
            using var s = Scope(staticWorld, UserRole.Owner);
            await s.ServiceProvider.GetRequiredService<UpdateCheckinSettingsHandler>()
                .HandleAsync(new UpdateCheckinSettingsRequest("Static"), default);
        }

        return new World(gym.Id, member.Id, userId, checkinMode);
    }

    private sealed record World(Guid GymId, Guid MemberId, Guid MemberUserId, CheckinMode Mode);
}
