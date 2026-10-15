using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.IntegrationTests;

/// <summary>
/// A5: ocupación del gym con permanencia asumida de 2 horas (corrige B4: el aforo se guardaba y nada lo leía).
/// Personas adentro = personas distintas con una entrada no anulada en la ventana. Base de carril: <c>gsm_test_puerta</c>.
/// </summary>
public sealed class OccupancyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 12);
    private static readonly DateTimeOffset Now = new GSM.Infrastructure.Common.SystemClock(
            Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
        .BusinessDayRange(Today).Start.AddHours(15);

    // ---- B4: reproduce el bug (con el aforo configurado, nadie calculaba qué tan lleno está) ----

    [Fact]
    public async Task B4_a_gym_with_capacity_reports_who_is_inside_and_how_full_it_is()
    {
        var gym = await SeedGymAsync(capacity: 40);
        for (var i = 0; i < 10; i++)
        {
            await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-30 - i));
        }

        var occupancy = await StaffOccupancyAsync(gym);

        Assert.Equal(10, occupancy.PeopleInside);
        Assert.Equal(40, occupancy.Capacity);
        Assert.Equal(25, occupancy.Percent);
        Assert.Equal("calm", occupancy.Level);
        Assert.Equal(120, occupancy.AssumedStayMinutes);
    }

    // ---- reglas de la ventana ----

    [Fact]
    public async Task A_person_with_two_entries_counts_once()
    {
        var gym = await SeedGymAsync(capacity: 20);
        var member = Guid.CreateVersion7();
        await EnterAsync(gym, member, Now.AddMinutes(-100));
        await EnterAsync(gym, member, Now.AddMinutes(-10));
        await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-5));

        Assert.Equal(2, (await StaffOccupancyAsync(gym)).PeopleInside);
    }

    [Fact]
    public async Task An_entry_from_two_hours_and_one_minute_ago_does_not_count_but_one_minute_less_does()
    {
        var gym = await SeedGymAsync(capacity: 20);
        await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-121));
        await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-119));

        Assert.Equal(1, (await StaffOccupancyAsync(gym)).PeopleInside);
    }

    [Fact]
    public async Task Voided_entries_do_not_count()
    {
        var gym = await SeedGymAsync(capacity: 20);
        var voided = await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-20));
        await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-20));

        using (var scope = Scope(gym))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var entry = await db.Set<AttendanceEntity>().FindAsync(voided);
            entry!.Void(Guid.CreateVersion7(), "Error de recepción", Now.AddMinutes(-10));
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, (await StaffOccupancyAsync(gym)).PeopleInside);
    }

    [Fact]
    public async Task Without_capacity_only_the_number_of_people_is_reported()
    {
        var gym = await SeedGymAsync(capacity: null);
        await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-20));
        await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-20));

        var occupancy = await StaffOccupancyAsync(gym);

        Assert.Equal(2, occupancy.PeopleInside);
        Assert.Null(occupancy.Capacity);
        Assert.Null(occupancy.Percent);
        Assert.Null(occupancy.Level);

        using var scope = Scope(gym, UserRole.Member);
        var mine = await scope.ServiceProvider.GetRequiredService<GetMyOccupancyHandler>().HandleAsync(default);
        Assert.Null(mine.Level);
    }

    [Fact]
    public async Task Another_gym_is_never_mixed_in()
    {
        var gym = await SeedGymAsync(capacity: 20);
        var other = await SeedGymAsync(capacity: 20);
        await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-20));
        await EnterAsync(other, Guid.CreateVersion7(), Now.AddMinutes(-20));
        await EnterAsync(other, Guid.CreateVersion7(), Now.AddMinutes(-20));

        Assert.Equal(1, (await StaffOccupancyAsync(gym)).PeopleInside);
        Assert.Equal(2, (await StaffOccupancyAsync(other)).PeopleInside);
    }

    // ---- niveles: tranquilo < 40 %, moderado 40-75 %, lleno > 75 % ----

    [Theory]
    [InlineData(0, "calm")]
    [InlineData(7, "calm")]
    [InlineData(8, "moderate")]
    [InlineData(15, "moderate")]
    [InlineData(16, "full")]
    [InlineData(25, "full")]
    public void Level_thresholds_on_a_capacity_of_twenty(int people, string expected)
        => Assert.Equal(expected, OccupancyLevel.For(people, 20));

    [Fact]
    public void Level_is_null_without_capacity()
        => Assert.Null(OccupancyLevel.For(10, null));

    [Fact]
    public async Task The_member_only_learns_the_level_never_the_number()
    {
        var gym = await SeedGymAsync(capacity: 10);
        for (var i = 0; i < 9; i++)
        {
            await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-15));
        }

        using var scope = Scope(gym, UserRole.Member);
        var mine = await scope.ServiceProvider.GetRequiredService<GetMyOccupancyHandler>().HandleAsync(default);

        Assert.Equal("full", mine.Level);
        Assert.Equal(["Level"], typeof(MemberOccupancyResponse).GetProperties().Select(p => p.Name));
    }

    // ---- permanencia configurable por gym ----

    [Fact]
    public async Task The_assumed_stay_is_configurable_per_gym_and_validated()
    {
        var gym = await SeedGymAsync(capacity: 20);
        await EnterAsync(gym, Guid.CreateVersion7(), Now.AddMinutes(-90));

        using (var scope = Scope(gym))
        {
            var saved = await scope.ServiceProvider.GetRequiredService<UpdateCheckinSettingsHandler>()
                .HandleAsync(new UpdateCheckinSettingsRequest("Rotating", null, 60), default);
            Assert.Equal(60, saved.AssumedStayMinutes);
        }

        var occupancy = await StaffOccupancyAsync(gym);
        Assert.Equal(0, occupancy.PeopleInside);
        Assert.Equal(60, occupancy.AssumedStayMinutes);

        using var bad = Scope(gym);
        var handler = bad.ServiceProvider.GetRequiredService<UpdateCheckinSettingsHandler>();
        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            handler.HandleAsync(new UpdateCheckinSettingsRequest("Rotating", null, 5), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            handler.HandleAsync(new UpdateCheckinSettingsRequest("Rotating", null, 481), default));
    }

    // ---- ayudantes ----

    private sealed record GymWorld(Guid GymId);

    private async Task<GymWorld> SeedGymAsync(int? capacity)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("Ocupación Gym", $"oc-{Guid.NewGuid():N}"[..16]);
        gym.UpdateCapacity(capacity);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();
        return new GymWorld(gym.Id);
    }

    private IServiceScope Scope(GymWorld gym, UserRole role = UserRole.Staff)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gym.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(Now, Today);
        return scope;
    }

    private async Task<Guid> EnterAsync(GymWorld gym, Guid memberId, DateTimeOffset at)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var entry = AttendanceEntity.Create(gym.GymId, memberId, AttendanceMethod.Manual, at, Today);
        db.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<OccupancyResponse> StaffOccupancyAsync(GymWorld gym)
    {
        using var scope = Scope(gym);
        return await scope.ServiceProvider.GetRequiredService<GetOccupancyHandler>().HandleAsync(default);
    }
}
