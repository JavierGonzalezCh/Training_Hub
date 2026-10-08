using GSM.Application.Attendance;
using GSM.Application.Notifications;
using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
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

/// <summary>Check-in del miembro con el codigo del gym, regla de gracia e intentos rechazados (etapa 12.2).</summary>
public sealed class MemberCheckinTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    [Fact]
    public async Task Member_needs_a_valid_code_of_their_own_gym_and_the_current_mode()
    {
        var w = await SeedAsync(endedDaysAgo: -10);
        var other = await SeedAsync(endedDaysAgo: -10);

        // Sin codigo.
        await Assert.ThrowsAsync<ValidationDomainException>(() => CheckInAsMember(w, null));

        // Codigo de otro gym.
        await Assert.ThrowsAsync<ValidationDomainException>(() => CheckInAsMember(w, CodeFor(other.GymId, CheckinMode.Rotating, 1, NowOf(Today))));

        // Codigo rotativo de hace dos ventanas (60+ s).
        var old = CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today).AddSeconds(-70));
        await Assert.ThrowsAsync<ValidationDomainException>(() => CheckInAsMember(w, old));

        // Un cartel fijo no sirve mientras el gym esta en modo rotativo.
        await Assert.ThrowsAsync<ValidationDomainException>(() => CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Static, 1, NowOf(Today))));

        // Y el valido entra.
        var (res, created) = await CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today)));
        Assert.True(created);
        Assert.Equal("Qr", res.Method);
        Assert.False(res.MembershipExpired);
    }

    [Fact]
    public async Task Static_code_of_a_previous_version_stops_working_after_regenerating()
    {
        var w = await SeedAsync(endedDaysAgo: -10);
        using (var s = Scope(w, UserRole.Owner))
        {
            var h = s.ServiceProvider.GetRequiredService<UpdateCheckinSettingsHandler>();
            await h.HandleAsync(new UpdateCheckinSettingsRequest("Static"), default);
        }

        var v1 = CodeFor(w.GymId, CheckinMode.Static, 1, NowOf(Today));
        using (var s = Scope(w, UserRole.Owner))
        {
            await s.ServiceProvider.GetRequiredService<RegenerateStaticCodeHandler>().HandleAsync(default);
        }

        await Assert.ThrowsAsync<ValidationDomainException>(() => CheckInAsMember(w, v1));
        var (_, created) = await CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Static, 2, NowOf(Today)));
        Assert.True(created);
    }

    [Fact]
    public async Task Member_gets_403_when_checkin_qr_is_off_but_staff_can_still_register_manually()
    {
        var w = await SeedAsync(endedDaysAgo: -10, checkinQr: false);

        await Assert.ThrowsAsync<ForbiddenDomainException>(
            () => CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today))));

        var (res, created) = await CheckInAsStaff(w);
        Assert.True(created);
        Assert.Equal("Manual", res.Method);
    }

    [Fact]
    public async Task Within_grace_the_member_enters_with_the_days_left()
    {
        var w = await SeedAsync(endedDaysAgo: 1);   // vencio ayer, gracia 3 -> quedan 2

        var (res, created) = await CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today)));

        Assert.True(created);
        Assert.True(res.MembershipExpired);
        Assert.Equal(2, res.GraceDaysLeft);
        Assert.Equal(Today.AddDays(-1), res.MembershipEndsOn);
        Assert.Equal(0, await PendingCountAsync(w));
    }

    [Fact]
    public async Task Past_grace_is_rejected_the_attempt_persists_and_repeats_instead_of_duplicating()
    {
        var w = await SeedAsync(endedDaysAgo: 4);
        var code = CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today));

        var first = await Assert.ThrowsAsync<ConflictException>(() => CheckInAsMember(w, code));
        Assert.Contains("venci", first.Message);
        await Assert.ThrowsAsync<ConflictException>(() => CheckInAsMember(w, code));

        using var s = Scope(w, UserRole.Staff);
        var page = await s.ServiceProvider.GetRequiredService<ListCheckinAttemptsHandler>()
            .HandleAsync(new ListCheckinAttemptsQuery { PendingOnly = true }, default);

        var item = Assert.Single(page.Items);
        Assert.Equal(2, item.AttemptCount);
        Assert.Equal("MembershipExpired", item.Reason);
        Assert.Equal(Today.AddDays(-4), item.MembershipEndedOn);
        Assert.Equal("Ana Prueba", item.MemberName);

        // Nunca entro: no hay asistencia.
        var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(0, await db.Set<GSM.Domain.Attendance.Attendance>().CountAsync(a => a.GymId == w.GymId));
    }

    [Fact]
    public async Task Member_without_any_membership_is_rejected_with_no_membership_reason()
    {
        var w = await SeedAsync(endedDaysAgo: null);

        await Assert.ThrowsAsync<ConflictException>(
            () => CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today))));

        using var s = Scope(w, UserRole.Staff);
        var page = await s.ServiceProvider.GetRequiredService<ListCheckinAttemptsHandler>()
            .HandleAsync(new ListCheckinAttemptsQuery(), default);
        Assert.Equal("NoMembership", Assert.Single(page.Items).Reason);
    }

    [Fact]
    public async Task Zero_grace_days_rejects_a_membership_that_ended_yesterday()
    {
        var w = await SeedAsync(endedDaysAgo: 1);
        using (var s = Scope(w, UserRole.Owner))
        {
            await s.ServiceProvider.GetRequiredService<UpdateMembershipSettingsHandler>()
                .HandleAsync(new UpdateMembershipSettingsRequest(0, 7), default);
        }

        await Assert.ThrowsAsync<ConflictException>(
            () => CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today))));
    }

    [Fact]
    public async Task Someone_who_already_checked_in_today_is_not_rejected_later_the_same_day()
    {
        var w = await SeedAsync(endedDaysAgo: 1);
        var code = CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today));
        await CheckInAsMember(w, code);

        // Pasa la gracia (se acorta a 0) y vuelve a escanear el mismo dia.
        using (var s = Scope(w, UserRole.Owner))
        {
            await s.ServiceProvider.GetRequiredService<UpdateMembershipSettingsHandler>()
                .HandleAsync(new UpdateMembershipSettingsRequest(0, 7), default);
        }

        var (res, created) = await CheckInAsMember(w, code);
        Assert.False(created);
        Assert.True(res.AlreadyCheckedInToday);
        Assert.Equal(0, await PendingCountAsync(w));
    }

    [Fact]
    public async Task Staff_needs_a_reason_to_let_an_expired_member_through_and_it_is_recorded()
    {
        var w = await SeedAsync(endedDaysAgo: 10);

        // Sin motivo: 422 con el mensaje que la UI usa para ofrecer "Renovar y cobrar" / "Dejar pasar".
        var ex = await Assert.ThrowsAsync<ValidationDomainException>(() => CheckInAsStaff(w));
        Assert.Contains("fuera de la gracia", ex.Message);
        Assert.Equal(0, await PendingCountAsync(w));

        var (res, created) = await CheckInAsStaff(w, "Paga mañana");

        Assert.True(created);
        Assert.True(res.Authorized);
        Assert.True(res.MembershipExpired);
        Assert.Equal(Today.AddDays(-10), res.MembershipEndsOn);
        Assert.Null(res.GraceDaysLeft);
        Assert.Equal(0, await PendingCountAsync(w));

        using var s = Scope(w, UserRole.Staff);
        var all = await s.ServiceProvider.GetRequiredService<ListAttendanceHandler>()
            .HandleAsync(new ListAttendanceQuery(), default);
        var authorized = await s.ServiceProvider.GetRequiredService<ListAttendanceHandler>()
            .HandleAsync(new ListAttendanceQuery { AuthorizedOnly = true }, default);
        var item = Assert.Single(authorized.Items);
        Assert.Equal(all.Total, authorized.Total);
        Assert.True(item.Authorized);
        Assert.Equal("Paga mañana", item.AuthorizationReason);
        Assert.NotNull(item.AuthorizedBy);
    }

    [Fact]
    public async Task Staff_enters_a_member_in_grace_without_a_reason_and_the_entry_is_not_authorized()
    {
        var w = await SeedAsync(endedDaysAgo: 1);

        var (res, created) = await CheckInAsStaff(w);

        Assert.True(created);
        Assert.False(res.Authorized);
        Assert.Equal(2, res.GraceDaysLeft);
        Assert.Contains("venció", res.Notice);
    }

    [Fact]
    public async Task Panel_counts_pending_attempts_and_resolving_clears_them_for_everyone()
    {
        var w = await SeedAsync(endedDaysAgo: 10);
        var code = CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today));
        await Assert.ThrowsAsync<ConflictException>(() => CheckInAsMember(w, code));
        Assert.Equal(1, await PendingCountAsync(w));

        // Un segundo miembro del staff ve el mismo aviso: es uno por gym.
        Guid noticeId;
        using (var s = Scope(w, UserRole.Staff))
        {
            var open = await s.ServiceProvider.GetRequiredService<ListStaffNotificationsHandler>()
                .HandleAsync(true, 1, 20, default);
            noticeId = open.Items.Single().Id;
            await s.ServiceProvider.GetRequiredService<ResolveStaffNotificationHandler>().HandleAsync(noticeId, default);
        }

        Assert.Equal(0, await PendingCountAsync(w));

        // El intento de Attendance queda visto por el evento de atención.
        using (var s = Scope(w, UserRole.Staff))
        {
            var attempt = (await s.ServiceProvider.GetRequiredService<ListCheckinAttemptsHandler>()
                .HandleAsync(new ListCheckinAttemptsQuery(), default)).Items.Single();
            Assert.NotNull(attempt.AcknowledgedAt);
        }

        // Un nuevo intento tras verlo crea fila nueva; resolve-all las vacía.
        await Assert.ThrowsAsync<ConflictException>(() => CheckInAsMember(w, code));
        Assert.Equal(1, await PendingCountAsync(w));
        using (var s = Scope(w, UserRole.Staff))
        {
            await s.ServiceProvider.GetRequiredService<ResolveAllStaffNotificationsHandler>().HandleAsync(default);
        }

        Assert.Equal(0, await PendingCountAsync(w));
    }

    private static DateTimeOffset NowOf(DateOnly day) => new SystemClockProbe().NoonOf(day);

    private string CodeFor(Guid gymId, CheckinMode mode, int version, DateTimeOffset now)
    {
        using var scope = factory.CreateScopeWithMigration();
        return scope.ServiceProvider.GetRequiredService<ICheckinCodeSigner>().Generate(gymId, mode, version, now);
    }

    private IServiceScope Scope(World w, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(role == UserRole.Member ? w.MemberUserId : Guid.CreateVersion7(), w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<(CheckInResponse Response, bool Created)> CheckInAsMember(World w, string? code)
    {
        using var s = Scope(w, UserRole.Member);
        return await s.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(null, code), default);
    }

    private async Task<(CheckInResponse Response, bool Created)> CheckInAsStaff(World w, string? authorizationReason = null)
    {
        using var s = Scope(w, UserRole.Staff);
        return await s.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(w.MemberId, null, authorizationReason), default);
    }

    private async Task<int> PendingCountAsync(World w)
    {
        using var s = Scope(w, UserRole.Staff);
        return (await s.ServiceProvider.GetRequiredService<GetStaffNotificationCountHandler>().HandleAsync(default)).Count;
    }

    /// <param name="endedDaysAgo">Dias desde el ultimo dia cubierto (negativo = aun vigente); <c>null</c> = sin membresia.</param>
    private async Task<World> SeedAsync(int? endedDaysAgo, bool checkinQr = true)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Member Checkin Gym", $"mc-{Guid.NewGuid():N}"[..16]);
        var flags = GymFeatureFlags.Default(gym.Id);
        if (!checkinQr)
        {
            flags.Update(true, true, true, true, true, false, true, false);
        }

        var userId = Guid.CreateVersion7();
        var member = Member.Create(gym.Id, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-100));
        member.LinkUser(userId);
        db.AddRange(gym, flags, member);

        if (endedDaysAgo is { } ago)
        {
            var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
            var end = Today.AddDays(-ago);
            db.AddRange(plan, Membership.Create(gym.Id, member.Id, plan.Id, end.AddDays(-29), 30));
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, member.Id, userId);
    }

    private sealed record World(Guid GymId, Guid MemberId, Guid MemberUserId);

    /// <summary>Mediodia de un dia de negocio, igual que <see cref="MutableClock.PinToBusinessDay"/>.</summary>
    private sealed class SystemClockProbe
    {
        public DateTimeOffset NoonOf(DateOnly day)
            => new GSM.Infrastructure.Common.SystemClock(
                    Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
                .BusinessDayRange(day).Start.AddHours(12);
    }
}
