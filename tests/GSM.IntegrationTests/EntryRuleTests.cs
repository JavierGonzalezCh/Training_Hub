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

/// <summary>A1b: entradas múltiples habilitadas por el staff (máximo diario y 2 horas entre entradas).</summary>
public sealed class EntryRuleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    private static DateTimeOffset NoonOf(DateOnly day)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(12);

    [Fact]
    public async Task Without_a_rule_the_second_entry_the_same_day_is_still_rejected()
    {
        var w = await SeedAsync();

        await EnterAsync(w, NoonOf(Today));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => EnterAsync(w, NoonOf(Today).AddHours(3)));
        Assert.Contains("ya entró hoy a las", ex.Message);
        Assert.Equal(1, await EntryCountAsync(w, Today));
    }

    [Fact]
    public async Task With_a_rule_of_two_the_second_entry_needs_two_hours_and_the_third_is_over_the_maximum()
    {
        var w = await SeedAsync();
        await SetRuleAsync(w, 2, "Entrena mañana y tarde");
        var noon = NoonOf(Today);

        await EnterAsync(w, noon);

        // Dentro de las 2 horas: rechazada y dice desde cuándo.
        var tooSoon = await Assert.ThrowsAsync<ConflictException>(() => EnterAsync(w, noon.AddMinutes(119)));
        Assert.Contains("a partir de las", tooSoon.Message);
        Assert.Equal(1, await EntryCountAsync(w, Today));

        // Pasadas las 2 horas entra.
        await EnterAsync(w, noon.AddHours(2));
        Assert.Equal(2, await EntryCountAsync(w, Today));

        // La tercera supera el máximo del día.
        var over = await Assert.ThrowsAsync<ConflictException>(() => EnterAsync(w, noon.AddHours(5)));
        Assert.Contains("máximo de 2 entradas", over.Message);
        Assert.Equal(2, await EntryCountAsync(w, Today));
    }

    [Fact]
    public async Task A_higher_maximum_allows_more_entries_each_two_hours_apart()
    {
        var w = await SeedAsync();
        await SetRuleAsync(w, 3, "Triatleta");
        var noon = NoonOf(Today);

        await EnterAsync(w, noon);
        await EnterAsync(w, noon.AddHours(2));
        await EnterAsync(w, noon.AddHours(4));
        await Assert.ThrowsAsync<ConflictException>(() => EnterAsync(w, noon.AddHours(6)));

        Assert.Equal(3, await EntryCountAsync(w, Today));
    }

    [Fact]
    public async Task The_member_by_qr_inside_the_gap_is_rejected_as_already_checked_in_without_ringing_the_bell()
    {
        var w = await SeedAsync();
        await SetRuleAsync(w, 2, "Entrena dos veces");
        var noon = NoonOf(Today);
        await EnterAsync(w, noon);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => EnterAsMemberAsync(w, noon.AddMinutes(30)));
        Assert.Contains("Podrás volver a entrar a partir de las", ex.Message);

        using var s = Scope(w, UserRole.Staff, noon.AddMinutes(30));
        var attempts = await s.ServiceProvider.GetRequiredService<ListCheckinAttemptsHandler>()
            .HandleAsync(new ListCheckinAttemptsQuery { Reason = "already_checked_in" }, default);
        Assert.Equal(1, Assert.Single(attempts.Items).AttemptCount);
        Assert.Empty((await s.ServiceProvider.GetRequiredService<ListCheckinAttemptsHandler>()
            .HandleAsync(new ListCheckinAttemptsQuery { PendingOnly = true }, default)).Items);
    }

    [Fact]
    public async Task Streak_and_visits_count_days_not_entries()
    {
        var w = await SeedAsync();
        await SetRuleAsync(w, 2, "Doble sesión");
        var noon = NoonOf(Today);
        await EnterAsync(w, noon);
        await EnterAsync(w, noon.AddHours(3));
        Assert.Equal(2, await EntryCountAsync(w, Today));

        using var s = Scope(w, UserRole.Staff, noon.AddHours(4));
        var reader = s.ServiceProvider.GetRequiredService<IAttendanceReader>();
        Assert.Equal(1, await reader.GetVisitCountAsync(w.MemberId));
        Assert.Equal(1, await reader.GetStreakAsync(w.MemberId));
        Assert.Single(await reader.GetAttendanceDatesAsync(w.MemberId));
    }

    [Fact]
    public async Task An_expired_rule_goes_back_to_one_entry_per_day()
    {
        var w = await SeedAsync();
        await SetRuleAsync(w, 2, "Temporada", validUntil: Today);

        // El último día vigente aún permite dos.
        await EnterAsync(w, NoonOf(Today));
        await EnterAsync(w, NoonOf(Today).AddHours(3));

        // Al día siguiente la regla venció: una sola.
        var tomorrow = Today.AddDays(1);
        await EnterAsync(w, NoonOf(tomorrow), tomorrow);
        await Assert.ThrowsAsync<ConflictException>(() => EnterAsync(w, NoonOf(tomorrow).AddHours(3), tomorrow));

        using var s = Scope(w, UserRole.Staff, NoonOf(tomorrow), tomorrow);
        var response = await s.ServiceProvider.GetRequiredService<GetEntryRuleHandler>().HandleAsync(w.MemberId, default);
        Assert.False(response.Enabled);
        Assert.True(response.Expired);
    }

    [Fact]
    public async Task Removing_the_rule_goes_back_to_one_entry_and_removing_twice_is_harmless()
    {
        var w = await SeedAsync();
        await SetRuleAsync(w, 2, "Provisional");
        await EnterAsync(w, NoonOf(Today));

        using (var s = Scope(w, UserRole.Staff, NoonOf(Today)))
        {
            var remove = s.ServiceProvider.GetRequiredService<RemoveEntryRuleHandler>();
            await remove.HandleAsync(w.MemberId, default);
            await remove.HandleAsync(w.MemberId, default);
        }

        await Assert.ThrowsAsync<ConflictException>(() => EnterAsync(w, NoonOf(Today).AddHours(3)));

        using var s2 = Scope(w, UserRole.Staff, NoonOf(Today));
        var response = await s2.ServiceProvider.GetRequiredService<GetEntryRuleHandler>().HandleAsync(w.MemberId, default);
        Assert.False(response.Enabled);
        Assert.False(response.Expired);
        Assert.Null(response.MaxDailyEntries);
    }

    [Fact]
    public async Task Setting_the_rule_twice_updates_the_same_row_and_records_who_and_why()
    {
        var w = await SeedAsync();
        var first = await SetRuleAsync(w, 2, "Primera vez");
        var second = await SetRuleAsync(w, 4, "Cambió el plan", validUntil: Today.AddDays(30));

        Assert.True(first.Enabled);
        Assert.Equal(4, second.MaxDailyEntries);
        Assert.Equal("Cambió el plan", second.Reason);
        Assert.Equal(Today.AddDays(30), second.ValidUntil);
        Assert.NotNull(second.GrantedBy);
        Assert.NotNull(second.GrantedAt);

        using var s = Scope(w, UserRole.Staff, NoonOf(Today));
        var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(1, await db.Set<MemberEntryRule>().CountAsync(r => r.MemberId == w.MemberId));
    }

    [Fact]
    public async Task The_rule_is_validated_maximum_from_two_to_five_reason_required_and_no_past_expiry()
    {
        var w = await SeedAsync();

        await Assert.ThrowsAsync<ValidationDomainException>(() => SetRuleAsync(w, 1, "Uno"));
        await Assert.ThrowsAsync<ValidationDomainException>(() => SetRuleAsync(w, 6, "Seis"));
        await Assert.ThrowsAsync<ValidationDomainException>(() => SetRuleAsync(w, 2, "  "));
        await Assert.ThrowsAsync<ValidationDomainException>(() => SetRuleAsync(w, 2, "Vencida", validUntil: Today.AddDays(-1)));

        // Un miembro inexistente (o de otro gym) es 404.
        using var s = Scope(w, UserRole.Staff, NoonOf(Today));
        await Assert.ThrowsAsync<NotFoundException>(() => s.ServiceProvider.GetRequiredService<SetEntryRuleHandler>()
            .HandleAsync(Guid.CreateVersion7(), new SetEntryRuleRequest(2, "Motivo válido", null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.ServiceProvider.GetRequiredService<GetEntryRuleHandler>()
            .HandleAsync(Guid.CreateVersion7(), default));
    }

    [Fact]
    public async Task A_rule_of_another_gym_is_invisible()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        await SetRuleAsync(w, 2, "Mío");

        using var s = Scope(other, UserRole.Staff, NoonOf(Today));
        await Assert.ThrowsAsync<NotFoundException>(() => s.ServiceProvider.GetRequiredService<GetEntryRuleHandler>()
            .HandleAsync(w.MemberId, default));
    }

    [Fact]
    public async Task Reception_card_reports_entries_today_the_maximum_and_when_the_member_can_come_back()
    {
        var w = await SeedAsync();
        await SetRuleAsync(w, 2, "Doble");
        var noon = NoonOf(Today);

        var before = await CardAsync(w, noon);
        Assert.True(before.CanRegister);
        Assert.Equal(0, before.EntriesToday);
        Assert.True(before.MultipleEntries);
        Assert.Equal(2, before.MaxDailyEntries);

        await EnterAsync(w, noon);

        // Dentro de las 2 horas: lleva 1, no se puede registrar y dice desde cuándo.
        var gap = await CardAsync(w, noon.AddMinutes(30));
        Assert.True(gap.AlreadyCheckedInToday);
        Assert.False(gap.CanRegister);
        Assert.Equal("already_checked_in", gap.RejectionReason);
        Assert.Equal(1, gap.EntriesToday);
        Assert.Equal(noon.AddHours(2), gap.NextEntryAt);

        // Pasadas las 2 horas se puede registrar otra aunque ya entró hoy.
        var later = await CardAsync(w, noon.AddHours(2));
        Assert.True(later.AlreadyCheckedInToday);
        Assert.True(later.CanRegister);
        Assert.Null(later.RejectionReason);
        Assert.Null(later.NextEntryAt);

        await EnterAsync(w, noon.AddHours(2));
        var full = await CardAsync(w, noon.AddHours(3));
        Assert.False(full.CanRegister);
        Assert.Equal(2, full.EntriesToday);
        Assert.Null(full.NextEntryAt);
    }

    [Fact]
    public async Task Reception_card_without_a_rule_reports_one_entry_per_day()
    {
        var w = await SeedAsync();
        var card = await CardAsync(w, NoonOf(Today));
        Assert.False(card.MultipleEntries);
        Assert.Equal(1, card.MaxDailyEntries);
        Assert.Equal(0, card.EntriesToday);
    }

    [Fact]
    public async Task A_second_entry_still_goes_through_the_membership_check()
    {
        var w = await SeedAsync();
        await SetRuleAsync(w, 2, "Doble");
        await EnterAsync(w, NoonOf(Today));

        // Entre una entrada y otra la membresía se congela: la segunda se rechaza por la membresía.
        using (var s = Scope(w, UserRole.Staff, NoonOf(Today)))
        {
            var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
            var frozen = await db.Set<Membership>().FirstAsync(m => m.MemberId == w.MemberId);
            db.Add(MembershipFreeze.Create(frozen.GymId, frozen.Id, Today, null, "Prueba", true, 0, null));
            await db.SaveChangesAsync();
        }

        // Recepción necesita el motivo de "Dejar pasar": 422 sin él.
        await Assert.ThrowsAsync<ValidationDomainException>(() => EnterAsync(w, NoonOf(Today).AddHours(3)));
        Assert.Equal(1, await EntryCountAsync(w, Today));
    }

    [Fact]
    public async Task Entry_rule_endpoints_require_a_session()
    {
        var w = await SeedAsync();
        var client = factory.CreateClient();

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/attendance/entry-rules/{w.MemberId}")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            (await client.DeleteAsync($"/api/attendance/entry-rules/{w.MemberId}")).StatusCode);
    }

    private IServiceScope Scope(World w, UserRole role, DateTimeOffset now, DateOnly? day = null)
    {
        var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(role == UserRole.Member ? w.MemberUserId : w.StaffUserId, w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(now, day ?? Today);
        return scope;
    }

    private async Task EnterAsync(World w, DateTimeOffset now, DateOnly? day = null)
    {
        using var s = Scope(w, UserRole.Staff, now, day);
        await s.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(w.MemberId, null), default);
    }

    private async Task EnterAsMemberAsync(World w, DateTimeOffset now)
    {
        using var s = Scope(w, UserRole.Member, now);
        var code = s.ServiceProvider.GetRequiredService<GSM.Application.Attendance.Abstractions.ICheckinCodeSigner>()
            .Generate(w.GymId, CheckinMode.Rotating, 1, now);
        await s.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(null, code), default);
    }

    private async Task<EntryRuleResponse> SetRuleAsync(World w, int max, string reason, DateOnly? validUntil = null)
    {
        using var s = Scope(w, UserRole.Staff, NoonOf(Today));
        return await s.ServiceProvider.GetRequiredService<SetEntryRuleHandler>()
            .HandleAsync(w.MemberId, new SetEntryRuleRequest(max, reason, validUntil), default);
    }

    private async Task<ReceptionCardResponse> CardAsync(World w, DateTimeOffset now)
    {
        using var s = Scope(w, UserRole.Staff, now);
        return await s.ServiceProvider.GetRequiredService<GetReceptionCardHandler>().HandleAsync(w.MemberId, default);
    }

    private async Task<int> EntryCountAsync(World w, DateOnly day)
    {
        using var s = Scope(w, UserRole.Staff, NoonOf(day), day);
        var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await db.Set<GSM.Domain.Attendance.Attendance>()
            .CountAsync(a => a.MemberId == w.MemberId && a.AttendanceDate == day);
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Entry Rule Gym", $"er-{Guid.NewGuid():N}"[..16]);
        var flags = GymFeatureFlags.Default(gym.Id);
        var userId = Guid.CreateVersion7();
        var member = Member.Create(gym.Id, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-100));
        member.LinkUser(userId);
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.AddRange(gym, flags, member, plan, Membership.Create(gym.Id, member.Id, plan.Id, Today.AddDays(-10), 30));
        await db.SaveChangesAsync();
        return new World(gym.Id, member.Id, userId, Guid.CreateVersion7());
    }

    private sealed record World(Guid GymId, Guid MemberId, Guid MemberUserId, Guid StaffUserId);
}
