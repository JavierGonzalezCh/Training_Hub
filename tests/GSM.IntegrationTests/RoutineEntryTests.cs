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
/// A8/C4 — un gym sin registro de entrada en la puerta es el que tiene el QR y el documento apagados; ahí el día
/// cuenta al registrar el primer ejercicio (ver <c>RemoteTrainingTests</c>).
/// </summary>
public sealed class RoutineEntryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    [Theory]
    [InlineData(true, false)]   // QR encendido
    [InlineData(false, true)]   // solo documento
    [InlineData(true, true)]
    public async Task With_any_door_registration_on_there_is_no_routine_entry(bool checkinQr, bool byDocument)
    {
        var w = await SeedAsync(endedDaysAgo: -10, checkinQr, byDocument);

        using var s = Scope(w, UserRole.Member);
        Assert.False(await s.ServiceProvider.GetRequiredService<IAttendanceReader>().IsRoutineEntryAsync(w.GymId));
    }

    [Fact]
    public async Task The_reader_reports_routine_entry_only_when_qr_and_document_are_both_off()
    {
        var w = await SeedAsync(endedDaysAgo: -10, checkinQr: false, byDocument: false);

        using var s = Scope(w, UserRole.Member);
        Assert.True(await s.ServiceProvider.GetRequiredService<IAttendanceReader>().IsRoutineEntryAsync(w.GymId));
    }

    private IServiceScope Scope(World w, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(role == UserRole.Member ? w.MemberUserId : Guid.CreateVersion7(), w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
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
