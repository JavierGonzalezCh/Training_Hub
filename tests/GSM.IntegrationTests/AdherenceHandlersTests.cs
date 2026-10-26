using GSM.Application.Routines;
using GSM.Application.Routines.Adherence;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>T6: cumplimiento de la rutina leído de la base (ficha del miembro y listado del staff).</summary>
public sealed class AdherenceHandlersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static DateOnly Day(int offset) => Monday.AddDays(offset);

    [Fact]
    public async Task Member_adherence_counts_days_from_the_logs_of_the_routine_in_force()
    {
        var w = await SeedGymAsync();
        var diligent = await SeedMemberAsync(w, "Diligente");
        var routine = await AssignAsync(w, diligent, onDay: 0);
        await LogAsync(w, diligent, routine.Days[0].Exercises[0].Id, 0);
        await LogAsync(w, diligent, routine.Days[1].Exercises[0].Id, 2);

        using var scope = AsStaff(w, Day(5));
        var result = await scope.ServiceProvider.GetRequiredService<GetMemberAdherenceHandler>()
            .HandleAsync(diligent, weeks: 2, default);

        Assert.True(result.HasRoutine);
        Assert.Equal(2, result.Weeks.Count);
        var current = result.Weeks[1];
        Assert.Equal(Monday, current.WeekStart);
        Assert.Equal(3, current.PlannedDays);
        Assert.Equal(2, current.DoneDays);
        Assert.Equal(67, current.CompliancePercent);
        Assert.Equal("done", current.Days![0].Status);
        Assert.Equal("missed", current.Days![4].Status);
        Assert.Null(result.Weeks[0].CompliancePercent); // la rutina empezó esta semana
        Assert.Null(result.Weeks[0].Days);
    }

    [Fact]
    public async Task Member_adherence_of_another_gym_member_is_not_found()
    {
        var w = await SeedGymAsync();

        using var scope = AsStaff(w, Day(5));
        await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
            .GetRequiredService<GetMemberAdherenceHandler>().HandleAsync(Guid.CreateVersion7(), 8, default));
    }

    [Fact]
    public async Task The_list_puts_the_most_behind_first_and_only_behind_hides_the_ones_on_track()
    {
        var w = await SeedGymAsync();
        var onTrack = await SeedMemberAsync(w, "Constante");
        var behind = await SeedMemberAsync(w, "Atrasado");
        var routineA = await AssignAsync(w, onTrack, onDay: 0);
        await AssignAsync(w, behind, onDay: 0);
        await LogAsync(w, onTrack, routineA.Days[0].Exercises[0].Id, 0);
        await LogAsync(w, onTrack, routineA.Days[1].Exercises[0].Id, 2);
        await LogAsync(w, onTrack, routineA.Days[2].Exercises[0].Id, 4);

        using var scope = AsStaff(w, Day(5));
        var handler = scope.ServiceProvider.GetRequiredService<ListRoutineAdherenceHandler>();

        var all = await handler.HandleAsync(null, onlyBehind: false, 1, 25, default);
        Assert.Equal(["Atrasado Prueba", "Constante Prueba"], all.Items.Select(i => i.FullName));
        Assert.Equal(0, all.Items[0].RecentPercent);
        Assert.Equal(100, all.Items[1].RecentPercent);
        Assert.Null(all.Items[0].LastLogOn);
        Assert.Equal(Day(4), all.Items[1].LastLogOn);

        var behindOnly = await handler.HandleAsync(null, onlyBehind: true, 1, 25, default);
        Assert.Single(behindOnly.Items);
        Assert.Equal(behind, behindOnly.Items[0].MemberId);
        Assert.Equal(1, behindOnly.Total);
    }

    private IServiceScope AsStaff(World w, DateOnly today)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);
        return scope;
    }

    private async Task<RoutineResponse> AssignAsync(World w, Guid memberId, int onDay)
    {
        using var scope = AsStaff(w, Day(onDay));
        var days = new[] { (1, "A"), (3, "B"), (5, "C") }
            .Select((d, i) => new RoutineDayInput(
                d.Item2, false, [new RoutineExerciseInput(w.CatalogIds[i], TestSets.Normal(3, 10), 60, null)], null, d.Item1))
            .ToList();
        return await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
            .HandleAsync(memberId, new SaveRoutineRequest("Semanal", null, days, "weekly"), default);
    }

    private async Task LogAsync(World w, Guid memberId, Guid routineExerciseId, int offset)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var date = Day(offset);
        var line = await db.Set<RoutineExercise>().IgnoreQueryFilters().SingleAsync(e => e.Id == routineExerciseId);
        db.Add(RoutineExerciseLog.Record(
            w.GymId, memberId, routineExerciseId, line.ExerciseId, date,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero), null));
        await db.SaveChangesAsync();
    }

    private async Task<World> SeedGymAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Adherence Gym", $"ad-{Guid.NewGuid():N}"[..14]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var catalog = await db.Set<Exercise>().Where(e => e.GymId == null)
            .OrderBy(e => e.Name).Select(e => e.Id).Take(4).ToListAsync();
        return new World(gym.Id, Guid.CreateVersion7(), catalog);
    }

    private async Task<Guid> SeedMemberAsync(World w, string firstName)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var member = Member.Create(w.GymId, firstName, "Prueba", $"doc-{Guid.NewGuid():N}"[..12], new DateOnly(2026, 1, 1));
        db.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    private sealed record World(Guid GymId, Guid StaffId, IReadOnlyList<Guid> CatalogIds);
}
