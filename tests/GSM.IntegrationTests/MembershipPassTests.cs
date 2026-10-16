using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Events;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Events;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Public;
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
/// MS5 — tiqueteras (Memberships v2, §3, §5.1, §7): plan de N entradas con vigencia, una entrada por cada
/// check-in (incluida la asistencia a clase), saldo, sin gracia, entradas anuladas que se devuelven.
/// </summary>
public sealed class MembershipPassTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static DateTimeOffset NoonOf(DateOnly day)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(12);

    // ---- calculador puro ----

    private static MembershipSpan Pass(int total, int used, DateOnly start, DateOnly end)
        => new(MembershipStatus.Active, start, end, null, total, used);

    [Fact]
    public void A_pass_with_entries_and_time_left_is_current_with_its_balance()
    {
        var access = CommercialStatusCalculator.Compute([Pass(12, 8, Today.AddDays(-10), Today.AddDays(20))], Today, 3, 7);

        Assert.Equal(CommercialStatus.Current, access.Commercial);
        Assert.Equal(AccessKind.Covered, access.Kind);
        Assert.Equal(4, access.EntriesLeft);
        Assert.Equal(12, access.EntriesTotal);
        Assert.Null(access.Notice);
    }

    [Fact]
    public void Two_entries_or_fewer_or_seven_days_or_fewer_is_expiring_with_the_pass_notice()
    {
        var fewEntries = CommercialStatusCalculator.Compute(
            [Pass(12, 10, Today.AddDays(-10), new DateOnly(2026, 11, 30))], Today, 3, 7);
        Assert.Equal(CommercialStatus.Expiring, fewEntries.Commercial);
        Assert.Equal("Te quedan 2 entradas de 12 (vence el 30 nov).", fewEntries.Notice);

        var lastOne = CommercialStatusCalculator.Compute(
            [Pass(12, 11, Today.AddDays(-10), new DateOnly(2026, 11, 30))], Today, 3, 7);
        Assert.Equal("Te queda 1 entrada de 12 (vence el 30 nov).", lastOne.Notice);

        var lastDays = CommercialStatusCalculator.Compute([Pass(12, 1, Today.AddDays(-25), Today.AddDays(5))], Today, 3, 7);
        Assert.Equal(CommercialStatus.Expiring, lastDays.Commercial);
        Assert.Equal(AccessKind.Expiring, lastDays.Kind);
        Assert.Contains("Te quedan 11 entradas de 12", lastDays.Notice);
    }

    [Fact]
    public void A_pass_with_no_entries_blocks_with_pass_exhausted_even_inside_its_validity()
    {
        var access = CommercialStatusCalculator.Compute([Pass(12, 12, Today.AddDays(-10), Today.AddDays(20))], Today, 3, 7);

        Assert.Equal(AccessKind.Blocked, access.Kind);
        Assert.False(access.CanEnter);
        Assert.Equal(AccessBlockReason.PassExhausted, access.BlockReason);
        Assert.Equal(0, access.EntriesLeft);
    }

    [Fact]
    public void A_pass_has_no_grace_after_its_validity_ends()
    {
        // Un día después de vencer, con 3 días de gracia configurados: una tiquetera no los recibe.
        var access = CommercialStatusCalculator.Compute([Pass(12, 3, Today.AddDays(-31), Today.AddDays(-1))], Today, 3, 7);

        Assert.Equal(CommercialStatus.Expired, access.Commercial);
        Assert.Equal(AccessBlockReason.Expired, access.BlockReason);
        Assert.False(access.CanEnter);
        Assert.Null(access.GraceDaysLeft);
    }

    [Fact]
    public void A_time_membership_covering_today_takes_precedence_and_grace_of_a_time_plan_still_counts()
    {
        var timeCovering = new MembershipSpan(MembershipStatus.Active, Today.AddDays(-5), Today.AddDays(25));
        var current = CommercialStatusCalculator.Compute(
            [timeCovering, Pass(12, 12, Today.AddDays(-10), Today.AddDays(20))], Today, 3, 7);
        Assert.Equal(CommercialStatus.Current, current.Commercial);
        Assert.Null(current.EntriesTotal);

        var timeInGrace = new MembershipSpan(MembershipStatus.Active, Today.AddDays(-31), Today.AddDays(-1));
        var grace = CommercialStatusCalculator.Compute(
            [timeInGrace, Pass(12, 12, Today.AddDays(-10), Today.AddDays(20))], Today, 3, 7);
        Assert.Equal(CommercialStatus.Grace, grace.Commercial);
    }

    // ---- planes ----

    [Fact]
    public async Task A_pass_plan_needs_entries_and_validity_and_a_time_plan_cannot_carry_entries()
    {
        var w = await SeedAsync();
        using var s = Scope(w, NoonOf(Today));
        var create = s.ServiceProvider.GetRequiredService<CreatePlanHandler>();

        var plan = await create.HandleAsync(
            new CreatePlanRequest("Tiquetera 12", null, 120000m, 1, null, Kind: "pass", Entries: 12, ValidityDays: 45), default);
        Assert.Equal("pass", plan.Kind);
        Assert.Equal(12, plan.Entries);
        Assert.Equal(45, plan.ValidityDays);
        Assert.Equal(45, plan.DurationValue);
        Assert.Equal("days", plan.DurationUnit);

        await Assert.ThrowsAsync<ValidationDomainException>(() => create.HandleAsync(
            new CreatePlanRequest("Sin entradas", null, 1m, 1, null, Kind: "pass", ValidityDays: 30), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() => create.HandleAsync(
            new CreatePlanRequest("Sin vigencia", null, 1m, 1, null, Kind: "pass", Entries: 5), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() => create.HandleAsync(
            new CreatePlanRequest("Por tiempo con entradas", null, 1m, 30, null, Entries: 5), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() => create.HandleAsync(
            new CreatePlanRequest("Tiquetera en meses", null, 1m, 1, "months", Kind: "pass", Entries: 5, ValidityDays: 30), default));

        // El tipo de un plan no cambia.
        await Assert.ThrowsAsync<ValidationDomainException>(() => s.ServiceProvider.GetRequiredService<UpdatePlanHandler>().HandleAsync(
            plan.Id, new UpdatePlanRequest("Tiquetera 12", null, 120000m, 30, Kind: "time"), default));

        // Se pueden cambiar las entradas y la vigencia de una tiquetera.
        var edited = await s.ServiceProvider.GetRequiredService<UpdatePlanHandler>().HandleAsync(
            plan.Id, new UpdatePlanRequest("Tiquetera 12", null, 130000m, 60, Entries: 15, ValidityDays: 60), default);
        Assert.Equal(15, edited.Entries);
        Assert.Equal(60, edited.ValidityDays);
    }

    [Fact]
    public async Task Assigning_a_pass_starts_today_with_its_entries_and_rejects_another_start_date()
    {
        var w = await SeedAsync();
        using var s = Scope(w, NoonOf(Today));
        var planId = await CreatePassPlanAsync(s.ServiceProvider, entries: 12, validityDays: 30);

        var preview = await s.ServiceProvider.GetRequiredService<PreviewMembershipHandler>()
            .HandleAsync(w.MemberId, planId, null, null, default);
        Assert.Equal(Today, preview.StartDate);
        Assert.Equal(Today.AddDays(29), preview.EndDate);

        await Assert.ThrowsAsync<ValidationDomainException>(() => s.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.MemberId, new AssignMembershipRequest(planId, Today.AddDays(3)), default));

        var membership = await s.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.MemberId, new AssignMembershipRequest(planId, null), default);
        Assert.Equal(Today, membership.StartDate);
        Assert.Equal(Today.AddDays(29), membership.EndDate);
        Assert.Equal("new", membership.RenewalKind);
        Assert.Equal(12, membership.EntriesTotal);
        Assert.Equal(0, membership.EntriesUsed);
        Assert.Equal(12, membership.EntriesLeft);

        var access = await s.ServiceProvider.GetRequiredService<IMembershipReader>().GetAccessStatusAsync(w.MemberId);
        Assert.Equal(12, access.EntriesLeft);
    }

    [Fact]
    public async Task A_pass_in_the_history_does_not_move_the_renewal_of_a_time_plan()
    {
        var w = await SeedAsync();
        using var s = Scope(w, NoonOf(Today));
        var passPlan = await CreatePassPlanAsync(s.ServiceProvider, 12, 60);
        await s.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.MemberId, new AssignMembershipRequest(passPlan, null), default);
        var timePlan = await CreateTimePlanAsync(s.ServiceProvider);

        var preview = await s.ServiceProvider.GetRequiredService<PreviewMembershipHandler>()
            .HandleAsync(w.MemberId, timePlan, null, null, default);

        // Sin la tiquetera en la cadena, el plan por tiempo arranca hoy (no tras el fin de la tiquetera).
        Assert.Equal(Today, preview.StartDate);
        Assert.Equal("new", preview.Kind);
    }

    // ---- consumo ----

    [Fact]
    public async Task Each_check_in_consumes_one_entry_and_the_response_shows_the_balance()
    {
        var w = await SeedAsync();
        await AssignPassAsync(w, entries: 5, validityDays: 30);

        var first = await CheckInAsync(w, NoonOf(Today));
        Assert.Equal(4, first.EntriesLeft);
        Assert.Equal(5, first.EntriesTotal);

        var second = await CheckInAsync(w, NoonOf(Today.AddDays(1)));
        Assert.Equal(3, second.EntriesLeft);
        Assert.Null(second.Notice);

        // Con 2 entradas o menos sale el letrero, ya contando la que acaba de gastar.
        var third = await CheckInAsync(w, NoonOf(Today.AddDays(2)));
        Assert.Equal(2, third.EntriesLeft);
        Assert.Contains("Te quedan 2 entradas de 5", third.Notice);

        using var s = Scope(w, NoonOf(Today.AddDays(3)));
        var access = await s.ServiceProvider.GetRequiredService<IMembershipReader>().GetAccessStatusAsync(w.MemberId);
        Assert.Equal(2, access.EntriesLeft);
        var history = await s.ServiceProvider.GetRequiredService<ListMemberMembershipsHandler>().HandleAsync(w.MemberId, default);
        var pass = Assert.Single(history);
        Assert.Equal(3, pass.EntriesUsed);
        Assert.Equal(2, pass.EntriesLeft);
    }

    [Fact]
    public async Task Consuming_is_idempotent_per_attendance_entry()
    {
        var w = await SeedAsync();
        await AssignPassAsync(w, 5, 30);
        var response = await CheckInAsync(w, NoonOf(Today));

        // Reprocesar el mismo evento (reintento de un manejador fallido) no descuenta otra entrada.
        using (var s = Scope(w, NoonOf(Today)))
        {
            var publisher = s.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
            await publisher.PublishAsync(new CheckInRegistered(response.AttendanceId, w.GymId, w.MemberId, Today), default);
            await publisher.PublishAsync(new CheckInRegistered(response.AttendanceId, w.GymId, w.MemberId, Today), default);
        }

        Assert.Equal(4, await BalanceAsync(w, NoonOf(Today)));
        using var db = Scope(w, NoonOf(Today));
        Assert.Equal(1, await db.ServiceProvider.GetRequiredService<GsmDbContext>().Set<PassUsage>().CountAsync(u => u.AttendanceId == response.AttendanceId));
    }

    [Fact]
    public async Task Multiple_entries_in_a_day_spend_one_entry_each()
    {
        var w = await SeedAsync();
        await AssignPassAsync(w, 5, 30);
        using (var s = Scope(w, NoonOf(Today)))
        {
            await s.ServiceProvider.GetRequiredService<SetEntryRuleHandler>()
                .HandleAsync(w.MemberId, new SetEntryRuleRequest(2, "Doble sesión", null), default);
        }

        await CheckInAsync(w, NoonOf(Today));
        var second = await CheckInAsync(w, NoonOf(Today).AddHours(3));

        Assert.Equal(3, second.EntriesLeft);
        Assert.Equal(3, await BalanceAsync(w, NoonOf(Today).AddHours(4)));
    }

    [Fact]
    public async Task Voiding_an_entry_returns_the_entry_to_the_pass_and_voiding_again_changes_nothing()
    {
        var w = await SeedAsync();
        await AssignPassAsync(w, 5, 30);
        var first = await CheckInAsync(w, NoonOf(Today));
        await CheckInAsync(w, NoonOf(Today.AddDays(1)));
        Assert.Equal(3, await BalanceAsync(w, NoonOf(Today.AddDays(1))));

        using (var s = Scope(w, NoonOf(Today.AddDays(1))))
        {
            await s.ServiceProvider.GetRequiredService<VoidCheckInHandler>()
                .HandleAsync(first.AttendanceId, new VoidCheckInRequest("Registré al miembro equivocado"), default);
        }

        Assert.Equal(4, await BalanceAsync(w, NoonOf(Today.AddDays(1))));

        // El mismo evento reprocesado no devuelve de más.
        using (var s = Scope(w, NoonOf(Today.AddDays(1))))
        {
            await s.ServiceProvider.GetRequiredService<IDomainEventPublisher>().PublishAsync(
                new CheckInVoided(
                    first.AttendanceId, w.GymId, w.MemberId, Today, NoonOf(Today), GSM.Domain.Attendance.AttendanceMethod.Manual,
                    w.StaffId, NoonOf(Today.AddDays(1)), "Registré al miembro equivocado"), default);
        }

        Assert.Equal(4, await BalanceAsync(w, NoonOf(Today.AddDays(1))));
    }

    [Fact]
    public async Task Attending_a_class_consumes_one_entry_unless_the_member_already_checked_in_that_day()
    {
        var w = await SeedAsync();
        await AssignPassAsync(w, 5, 30);

        // Sin entrada ese día: la asistencia a clase crea el check-in y consume.
        using (var s = Scope(w, NoonOf(Today)))
        {
            await s.ServiceProvider.GetRequiredService<IDomainEventPublisher>()
                .PublishAsync(new ClassAttendanceRegistered(w.MemberId, w.GymId, Today), default);
        }

        Assert.Equal(4, await BalanceAsync(w, NoonOf(Today)));

        // Misma fecha otra vez (ya hay entrada ese día): no consume de nuevo.
        using (var s = Scope(w, NoonOf(Today)))
        {
            await s.ServiceProvider.GetRequiredService<IDomainEventPublisher>()
                .PublishAsync(new ClassAttendanceRegistered(w.MemberId, w.GymId, Today), default);
        }

        Assert.Equal(4, await BalanceAsync(w, NoonOf(Today)));

        // Quien ya fichó al entrar y luego asiste a la clase gasta una sola entrada ese día.
        await CheckInAsync(w, NoonOf(Today.AddDays(1)));
        using (var s = Scope(w, NoonOf(Today.AddDays(1))))
        {
            await s.ServiceProvider.GetRequiredService<IDomainEventPublisher>()
                .PublishAsync(new ClassAttendanceRegistered(w.MemberId, w.GymId, Today.AddDays(1)), default);
        }

        Assert.Equal(3, await BalanceAsync(w, NoonOf(Today.AddDays(1))));
    }

    // ---- agotada, vencida y sin gracia ----

    [Fact]
    public async Task An_exhausted_pass_blocks_reception_which_can_let_the_member_through_without_going_negative()
    {
        var w = await SeedAsync();
        await AssignPassAsync(w, 1, 30);
        await CheckInAsync(w, NoonOf(Today));

        using (var s = Scope(w, NoonOf(Today.AddDays(1))))
        {
            var card = await s.ServiceProvider.GetRequiredService<GetReceptionCardHandler>().HandleAsync(w.MemberId, default);
            Assert.Equal("pass_exhausted", card.RejectionReason);
            Assert.True(card.CanLetThrough);
            Assert.False(card.CanRegister);
            Assert.Equal(0, card.EntriesLeft);
            Assert.Equal(1, card.EntriesTotal);

            // Sin motivo no pasa; con motivo queda como entrada autorizada.
            await Assert.ThrowsAsync<ValidationDomainException>(() => s.ServiceProvider.GetRequiredService<CheckInHandler>()
                .HandleAsync(new CheckInRequest(w.MemberId, null), default));
            var (letThrough, _) = await s.ServiceProvider.GetRequiredService<CheckInHandler>()
                .HandleAsync(new CheckInRequest(w.MemberId, null, "Cortesía del dueño"), default);
            Assert.True(letThrough.Authorized);
        }

        // El saldo no baja de cero.
        Assert.Equal(0, await BalanceAsync(w, NoonOf(Today.AddDays(1))));
    }

    [Fact]
    public async Task An_exhausted_pass_gives_the_pass_exhausted_verdict_that_rejects_a_member_scan()
    {
        var w = await SeedAsync();
        await AssignPassAsync(w, 1, 30);
        await CheckInAsync(w, NoonOf(Today));

        using var s = Scope(w, NoonOf(Today.AddDays(1)));
        var access = await s.ServiceProvider.GetRequiredService<IMembershipReader>().GetAccessStatusAsync(w.MemberId);

        Assert.Equal(AccessKind.Blocked, access.Kind);
        Assert.Equal(AccessBlockReason.PassExhausted, access.BlockReason);
        Assert.Equal("pass_exhausted", GSM.Domain.Members.EnumText.Snake(access.BlockReason));
    }

    [Fact]
    public async Task A_pass_past_its_validity_blocks_with_no_grace_and_the_leftover_entries_are_lost()
    {
        var w = await SeedAsync();
        await AssignPassAsync(w, 10, 5); // vigente hoy..hoy+4

        // Un día después de vencer, aunque el gym dé 3 días de gracia por defecto.
        using var s = Scope(w, NoonOf(Today.AddDays(5)));
        var access = await s.ServiceProvider.GetRequiredService<IMembershipReader>().GetAccessStatusAsync(w.MemberId);
        Assert.Equal(CommercialStatus.Expired, access.Commercial);
        Assert.False(access.CanEnter);
        Assert.Null(access.GraceDaysLeft);

        // Recepción puede dejarlo pasar con motivo, y eso no consume (la tiquetera ya no cubre ese día).
        var (letThrough, _) = await s.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(w.MemberId, null, "Cortesía"), default);
        Assert.True(letThrough.Authorized);
    }

    [Fact]
    public async Task The_balance_shows_in_the_member_card_through_the_access_status_response()
    {
        var w = await SeedAsync();
        await AssignPassAsync(w, 12, 30);
        await CheckInAsync(w, NoonOf(Today));

        using var s = Scope(w, NoonOf(Today.AddDays(1)));
        var access = await s.ServiceProvider.GetRequiredService<IMembershipReader>().GetAccessStatusAsync(w.MemberId);
        var response = MembershipMapper.ToResponse(access);

        Assert.Equal(11, response.EntriesLeft);
        Assert.Equal(12, response.EntriesTotal);
    }

    // ---- congelar y recordatorios ----

    [Fact]
    public async Task A_pass_can_be_frozen_which_moves_its_validity_but_not_its_entries()
    {
        var w = await SeedAsync();
        using (var s = Scope(w, NoonOf(Today)))
        {
            var plan = await s.ServiceProvider.GetRequiredService<CreatePlanHandler>().HandleAsync(
                new CreatePlanRequest($"Tiq {Guid.NewGuid():N}"[..14], null, 90000m, 30, null, 10, 1, "pass", 8, 30), default);
            var membership = await s.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(w.MemberId, new AssignMembershipRequest(plan.Id, null), default);
            var frozen = await s.ServiceProvider.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                w.MemberId, membership.Id, new CreateFreezeRequest(Today.AddDays(2), Today.AddDays(6), "Viaje"), default);

            Assert.Equal(Today.AddDays(29 + 5), frozen.EndDate);
            Assert.Equal(8, frozen.EntriesTotal);
            Assert.Equal(8, frozen.EntriesLeft);
        }

        using var during = Scope(w, NoonOf(Today.AddDays(3)));
        var access = await during.ServiceProvider.GetRequiredService<IMembershipReader>().GetAccessStatusAsync(w.MemberId);
        Assert.Equal(CommercialStatus.Frozen, access.Commercial);
    }

    [Fact]
    public async Task Reminder_candidates_exclude_a_pass_already_expired_and_give_passes_no_grace()
    {
        var w = await SeedAsync();
        await AssignPassAsync(w, 10, 4); // vigente hoy..hoy+3

        // Quedan 3 días: candidata, sin días de gracia (no hay aviso de "último día de gracia", CO7).
        using (var s = Scope(w, NoonOf(Today)))
        {
            var candidates = await s.ServiceProvider.GetRequiredService<IMembershipReader>().GetReminderCandidatesAsync(w.GymId, 7);
            var candidate = Assert.Single(candidates);
            Assert.Equal(3, candidate.DaysLeft);
            Assert.Equal(0, candidate.GraceDays);
        }

        // Venció ayer: una de tiempo seguiría en la ventana de gracia; una tiquetera ya no es candidata.
        using (var s = Scope(w, NoonOf(Today.AddDays(5))))
        {
            var candidates = await s.ServiceProvider.GetRequiredService<IMembershipReader>().GetReminderCandidatesAsync(w.GymId, 7);
            Assert.Empty(candidates);
        }
    }

    // ---- soporte ----

    private sealed record World(Guid GymId, Guid MemberId, Guid StaffId);

    private IServiceScope Scope(World w, DateTimeOffset now)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(now, DateOnly.FromDateTime(now.UtcDateTime.AddHours(-5)));
        return scope;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Pass Gym", $"ps-{Guid.NewGuid():N}"[..16]);
        var member = Member.Create(gym.Id, "Ana", "Pase", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-100));
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), member);
        await db.SaveChangesAsync();
        return new World(gym.Id, member.Id, Guid.CreateVersion7());
    }

    private static async Task<Guid> CreatePassPlanAsync(IServiceProvider sp, int entries, int validityDays)
        => (await sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest(
                $"Tiquetera {Guid.NewGuid():N}"[..20], null, 100000m, validityDays, null, Kind: "pass", Entries: entries,
                ValidityDays: validityDays), default)).Id;

    private static async Task<Guid> CreateTimePlanAsync(IServiceProvider sp)
        => (await sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest($"Mensual {Guid.NewGuid():N}"[..20], null, 80000m, 30), default)).Id;

    private async Task AssignPassAsync(World w, int entries, int validityDays)
    {
        using var s = Scope(w, NoonOf(Today));
        var planId = await CreatePassPlanAsync(s.ServiceProvider, entries, validityDays);
        await s.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.MemberId, new AssignMembershipRequest(planId, null), default);
    }

    private async Task<CheckInResponse> CheckInAsync(World w, DateTimeOffset now)
    {
        using var s = Scope(w, now);
        var (response, _) = await s.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(w.MemberId, null), default);
        return response;
    }

    private async Task<int?> BalanceAsync(World w, DateTimeOffset now)
    {
        using var s = Scope(w, now);
        return (await s.ServiceProvider.GetRequiredService<IMembershipReader>().GetAccessStatusAsync(w.MemberId)).EntriesLeft;
    }
}
