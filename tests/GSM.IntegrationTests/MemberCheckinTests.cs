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
        Assert.Equal("membership_expired", item.Reason);
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
        Assert.Equal("no_membership", Assert.Single(page.Items).Reason);
    }

    [Fact]
    public async Task Zero_grace_days_rejects_a_membership_that_ended_yesterday()
    {
        var w = await SeedAsync(endedDaysAgo: 1);
        using (var s = Scope(w, UserRole.Owner))
        {
            await s.ServiceProvider.GetRequiredService<UpdateMembershipSettingsHandler>()
                .HandleAsync(new UpdateMembershipSettingsRequest(0, 7, 0m), default);
        }

        await Assert.ThrowsAsync<ConflictException>(
            () => CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today))));
    }

    [Fact]
    public async Task Second_qr_attempt_the_same_day_is_rejected_with_already_checked_in_and_never_rings_the_bell()
    {
        var w = await SeedAsync(endedDaysAgo: -10);
        var code = CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today));
        await CheckInAsMember(w, code);

        var first = await Assert.ThrowsAsync<ConflictException>(() => CheckInAsMember(w, code));
        Assert.Contains("Ya registraste tu entrada hoy a las", first.Message);
        await Assert.ThrowsAsync<ConflictException>(() => CheckInAsMember(w, code));
        await Assert.ThrowsAsync<ConflictException>(() => CheckInAsMember(w, code));

        using var s = Scope(w, UserRole.Staff);
        var list = s.ServiceProvider.GetRequiredService<ListCheckinAttemptsHandler>();

        // Un solo registro que cuenta las repeticiones (3 o más: el panel lo resalta).
        var byReason = await list.HandleAsync(new ListCheckinAttemptsQuery { Reason = "already_checked_in" }, default);
        var item = Assert.Single(byReason.Items);
        Assert.Equal("already_checked_in", item.Reason);
        Assert.Equal(3, item.AttemptCount);

        // No es accionable: ni campana ni lista de pendientes; una sola asistencia.
        Assert.Equal(0, await PendingCountAsync(w));
        Assert.Empty((await list.HandleAsync(new ListCheckinAttemptsQuery { PendingOnly = true }, default)).Items);
        var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(1, await db.Set<GSM.Domain.Attendance.Attendance>().CountAsync(a => a.GymId == w.GymId));
    }

    [Fact]
    public async Task Staff_cannot_register_a_second_entry_the_same_day_even_with_a_reason()
    {
        var w = await SeedAsync(endedDaysAgo: -10);
        await CheckInAsStaff(w);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CheckInAsStaff(w, "Otra vez"));
        Assert.Contains("ya entró hoy a las", ex.Message);

        using var s = Scope(w, UserRole.Staff);
        var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(1, await db.Set<GSM.Domain.Attendance.Attendance>().CountAsync(a => a.GymId == w.GymId));
        // Recepción no deja intento: el staff no "insiste", solo mira la tarjeta.
        Assert.Equal(0, await db.Set<CheckinAttempt>().CountAsync(a => a.GymId == w.GymId));
    }

    [Fact]
    public async Task Someone_who_entered_today_is_rejected_as_already_checked_in_not_for_membership()
    {
        var w = await SeedAsync(endedDaysAgo: 1);
        await CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today)));

        // Pasa la gracia (se acorta a 0) y escanea de nuevo: el motivo es "ya entró", no la membresía.
        using (var s = Scope(w, UserRole.Owner))
        {
            await s.ServiceProvider.GetRequiredService<UpdateMembershipSettingsHandler>()
                .HandleAsync(new UpdateMembershipSettingsRequest(0, 7, 0m), default);
        }

        await Assert.ThrowsAsync<ConflictException>(
            () => CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today))));

        using var s2 = Scope(w, UserRole.Staff);
        var list = s2.ServiceProvider.GetRequiredService<ListCheckinAttemptsHandler>();
        Assert.Equal("already_checked_in", Assert.Single((await list.HandleAsync(new ListCheckinAttemptsQuery(), default)).Items).Reason);
        Assert.Equal(0, await PendingCountAsync(w));
    }

    [Fact]
    public async Task Frozen_membership_is_rejected_as_membership_frozen_and_staff_can_let_it_through()
    {
        var w = await SeedAsync(endedDaysAgo: -10);
        using (var s = Scope(w, UserRole.Staff))
        {
            var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
            var membership = await db.Set<Membership>().FirstAsync(m => m.MemberId == w.MemberId);
            db.Add(MembershipFreeze.Create(membership.GymId, membership.Id, Today, null, "Prueba", true, 0, null));
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today))));
        Assert.Contains("congelada", ex.Message);

        using (var s = Scope(w, UserRole.Staff))
        {
            var attempt = Assert.Single((await s.ServiceProvider.GetRequiredService<ListCheckinAttemptsHandler>()
                .HandleAsync(new ListCheckinAttemptsQuery { Reason = "membership_frozen" }, default)).Items);
            Assert.Equal("membership_frozen", attempt.Reason);
        }

        // Recepción: sin motivo 422, con motivo entra como entrada autorizada.
        await Assert.ThrowsAsync<ValidationDomainException>(() => CheckInAsStaff(w));
        var (res, _) = await CheckInAsStaff(w, "El dueño lo autorizó");
        Assert.True(res.Authorized);
    }

    [Fact]
    public async Task Inactive_member_is_rejected_by_qr_with_a_recorded_attempt_and_staff_cannot_let_through()
    {
        var w = await SeedAsync(endedDaysAgo: -10);
        await DeactivateAsync(w);

        await Assert.ThrowsAsync<ConflictException>(
            () => CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today))));
        // "Dejar pasar" no aplica a un miembro dado de baja: primero se reactiva la ficha.
        await Assert.ThrowsAsync<ConflictException>(() => CheckInAsStaff(w, "Motivo cualquiera"));

        using var s2 = Scope(w, UserRole.Staff);
        var attempt = Assert.Single((await s2.ServiceProvider.GetRequiredService<ListCheckinAttemptsHandler>()
            .HandleAsync(new ListCheckinAttemptsQuery { Reason = "member_inactive" }, default)).Items);
        Assert.Equal(1, attempt.AttemptCount);
    }

    [Fact]
    public async Task Scheduled_membership_is_rejected_by_qr_and_reception_can_let_it_through_with_a_reason()
    {
        // Empieza mañana (Today + 1): hoy no cubre nada.
        var w = await SeedAsync(endedDaysAgo: -30);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => CheckInAsMember(w, CodeFor(w.GymId, CheckinMode.Rotating, 1, NowOf(Today))));
        Assert.Contains("empieza el", ex.Message);

        var card = await CardOf(w);
        Assert.Equal("scheduled", card.Commercial);
        Assert.False(card.CanRegister);
        Assert.True(card.CanLetThrough);
        Assert.Equal("membership_scheduled", card.RejectionReason);
        Assert.Equal("Programada: empieza el 6 oct.", card.BlockMessage);

        await Assert.ThrowsAsync<ValidationDomainException>(() => CheckInAsStaff(w));
        var (res, _) = await CheckInAsStaff(w, "Paga mañana, el dueño lo autorizó");
        Assert.True(res.Authorized);
    }

    [Fact]
    public async Task Unknown_reason_filter_is_a_422()
    {
        var w = await SeedAsync(endedDaysAgo: -10);
        using var s = Scope(w, UserRole.Staff);
        await Assert.ThrowsAsync<ValidationDomainException>(() => s.ServiceProvider.GetRequiredService<ListCheckinAttemptsHandler>()
            .HandleAsync(new ListCheckinAttemptsQuery { Reason = "nope" }, default));
    }

    [Fact]
    public async Task Reception_card_reports_each_case_without_registering_anything()
    {
        var covered = await SeedAsync(endedDaysAgo: -10);
        var expired = await SeedAsync(endedDaysAgo: 10);
        var none = await SeedAsync(endedDaysAgo: null);

        var ok = await CardOf(covered);
        Assert.True(ok.CanRegister);
        Assert.False(ok.CanLetThrough);
        Assert.Null(ok.RejectionReason);
        Assert.Equal("Ana Prueba", ok.MemberName);

        var blocked = await CardOf(expired);
        Assert.False(blocked.CanRegister);
        Assert.True(blocked.CanLetThrough);
        Assert.Equal("membership_expired", blocked.RejectionReason);

        var noPlan = await CardOf(none);
        Assert.True(noPlan.CanLetThrough);
        Assert.Equal("no_membership", noPlan.RejectionReason);

        // Ver la tarjeta no registra nada ni deja intentos.
        using var s2 = Scope(covered, UserRole.Staff);
        var db = s2.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(0, await db.Set<GSM.Domain.Attendance.Attendance>().CountAsync(a => a.GymId == covered.GymId));
        Assert.Equal(0, await db.Set<CheckinAttempt>().CountAsync(a => a.GymId == covered.GymId));
    }

    [Fact]
    public async Task Reception_card_after_entering_says_already_checked_in_and_offers_nothing()
    {
        var w = await SeedAsync(endedDaysAgo: -10);
        await CheckInAsStaff(w);

        var card = await CardOf(w);

        Assert.True(card.AlreadyCheckedInToday);
        Assert.NotNull(card.CheckedInAt);
        Assert.False(card.CanRegister);
        Assert.False(card.CanLetThrough);
        Assert.Equal("already_checked_in", card.RejectionReason);
    }

    [Fact]
    public async Task Reception_card_for_an_inactive_member_offers_no_let_through()
    {
        var w = await SeedAsync(endedDaysAgo: 10);
        await DeactivateAsync(w);

        var card = await CardOf(w);
        Assert.False(card.MemberActive);
        Assert.False(card.CanRegister);
        Assert.False(card.CanLetThrough);
        Assert.Equal("member_inactive", card.RejectionReason);
    }

    [Fact]
    public async Task Reception_card_is_for_staff_only_over_http()
    {
        var w = await SeedAsync(endedDaysAgo: -10);
        var client = factory.CreateClient();

        // Sin token: 401.
        var anonymous = await client.GetAsync($"/api/attendance/reception/{w.MemberId}");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private async Task<ReceptionCardResponse> CardOf(World w)
    {
        using var s = Scope(w, UserRole.Staff);
        return await s.ServiceProvider.GetRequiredService<GetReceptionCardHandler>().HandleAsync(w.MemberId, default);
    }

    private async Task DeactivateAsync(World w)
    {
        using var s = Scope(w, UserRole.Staff);
        var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
        (await db.Set<Member>().FirstAsync(m => m.Id == w.MemberId)).Deactivate();
        await db.SaveChangesAsync();
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
