using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
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
/// A8 — en un gym sin registro de entrada en la puerta (QR y documento apagados), el miembro cuenta su día al
/// iniciar el entrenamiento: una entrada con método <c>routine</c> que suma a visitas, racha y logros.
/// </summary>
public sealed class RoutineEntryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    [Fact]
    public async Task Without_any_door_registration_starting_the_training_counts_the_day_once()
    {
        var w = await SeedAsync(endedDaysAgo: -10, checkinQr: false, byDocument: false);

        var (first, created) = await StartAsync(w);
        Assert.True(created);
        Assert.Equal("Routine", first.Method);
        Assert.True(first.CountsForProgress);

        // Iniciar la rutina otra vez el mismo día devuelve la misma entrada: no suma una segunda visita.
        var (again, createdAgain) = await StartAsync(w);
        Assert.False(createdAgain);
        Assert.Equal(first.AttendanceId, again.AttendanceId);

        using var s = Scope(w, UserRole.Member);
        var reader = s.ServiceProvider.GetRequiredService<IAttendanceReader>();
        Assert.Equal(1, await reader.GetVisitCountAsync(w.MemberId));
        var saved = await s.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<GSM.Domain.Attendance.Attendance>().SingleAsync(a => a.MemberId == w.MemberId);
        Assert.Equal(AttendanceMethod.Routine, saved.Method);
    }

    [Theory]
    [InlineData(true, false)]   // QR encendido
    [InlineData(false, true)]   // solo documento
    [InlineData(true, true)]
    public async Task With_any_door_registration_on_starting_the_training_is_forbidden(bool checkinQr, bool byDocument)
    {
        var w = await SeedAsync(endedDaysAgo: -10, checkinQr, byDocument);

        var ex = await Assert.ThrowsAsync<ForbiddenDomainException>(() => StartAsync(w));
        Assert.Contains("recepción", ex.Message);

        using var s = Scope(w, UserRole.Member);
        Assert.False(await s.ServiceProvider.GetRequiredService<IAttendanceReader>().IsRoutineEntryAsync(w.GymId));
        Assert.False(await s.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<GSM.Domain.Attendance.Attendance>().AnyAsync(a => a.MemberId == w.MemberId));
    }

    [Fact]
    public async Task The_reader_reports_routine_entry_only_when_qr_and_document_are_both_off()
    {
        var w = await SeedAsync(endedDaysAgo: -10, checkinQr: false, byDocument: false);

        using var s = Scope(w, UserRole.Member);
        Assert.True(await s.ServiceProvider.GetRequiredService<IAttendanceReader>().IsRoutineEntryAsync(w.GymId));
    }

    [Fact]
    public async Task Within_grace_it_counts_and_past_grace_it_is_rejected_like_the_qr()
    {
        var inGrace = await SeedAsync(endedDaysAgo: 1, checkinQr: false, byDocument: false);
        var (res, created) = await StartAsync(inGrace);
        Assert.True(created);
        Assert.Equal(2, res.GraceDaysLeft);

        var expired = await SeedAsync(endedDaysAgo: 4, checkinQr: false, byDocument: false);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => StartAsync(expired));
        Assert.Contains("recepción", ex.Message);

        using var s = Scope(expired, UserRole.Member);
        Assert.False(await s.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<GSM.Domain.Attendance.Attendance>().AnyAsync(a => a.MemberId == expired.MemberId));
    }

    [Fact]
    public async Task Staff_cannot_start_a_training_entry()
    {
        var w = await SeedAsync(endedDaysAgo: -10, checkinQr: false, byDocument: false);

        using var s = Scope(w, UserRole.Staff);
        await Assert.ThrowsAsync<ForbiddenDomainException>(
            () => s.ServiceProvider.GetRequiredService<CheckInHandler>().HandleRoutineStartAsync(default));
    }

    private IServiceScope Scope(World w, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(role == UserRole.Member ? w.MemberUserId : Guid.CreateVersion7(), w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<(CheckInResponse Response, bool Created)> StartAsync(World w)
    {
        using var s = Scope(w, UserRole.Member);
        return await s.ServiceProvider.GetRequiredService<CheckInHandler>().HandleRoutineStartAsync(default);
    }

    /// <param name="endedDaysAgo">Días desde el último día cubierto (negativo = aún vigente).</param>
    private async Task<World> SeedAsync(int endedDaysAgo, bool checkinQr, bool byDocument)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Routine Entry Gym", $"re-{Guid.NewGuid():N}"[..16]);
        var flags = GymFeatureFlags.Default(gym.Id);
        if (!checkinQr)
        {
            flags.Update(true, true, true, true, true, false, false);
        }

        var settings = CheckinSettings.Default(gym.Id);
        settings.Update(CheckinMode.Rotating, byDocument);

        var userId = Guid.CreateVersion7();
        var member = Member.Create(gym.Id, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-100));
        member.LinkUser(userId);

        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        var end = Today.AddDays(-endedDaysAgo);
        db.AddRange(gym, flags, settings, member, plan, Membership.Create(gym.Id, member.Id, plan.Id, end.AddDays(-29), 30));

        await db.SaveChangesAsync();
        return new World(gym.Id, member.Id, userId);
    }

    private sealed record World(Guid GymId, Guid MemberId, Guid MemberUserId);
}
