using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using GSM.Application.Payments.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// MS6 — cambio de plan con crédito (Memberships v2 §8): la vigente se cancela hoy ("Cambio de plan"), el crédito es lo
/// pagado × lo que queda ÷ el total (días o entradas), la nueva empieza hoy con <c>CreditApplied</c>, y si el crédito
/// supera el precio nuevo el excedente se devuelve con un pago negativo. Los pagos de la anterior no se anulan.
/// </summary>
public sealed class MembershipPlanChangeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day0 = new(2026, 3, 10);

    private sealed record World(Guid GymId, Guid StaffId, Guid MemberId);

    // ---- calculador puro ----

    [Fact]
    public void The_pure_credit_prorates_the_paid_base_by_the_days_left_and_never_exceeds_what_was_paid()
    {
        var membership = Membership.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Day0, 30, pricing: new MembershipPricing(90_000m, null, 30_000m, null));

        // Pagó 120.000 (plan + matrícula): la base es el precio del plan, la matrícula no se devuelve.
        var half = PlanChangeCredit.Compute(membership, [], 0, 120_000m, Day0.AddDays(15));
        Assert.Equal(90_000m, half.Base);
        Assert.Equal(15, half.DaysLeft);
        Assert.Equal(30, half.DaysTotal);
        Assert.Equal(45_000m, half.Calculated);

        // Último día: queda 1 de 30.
        Assert.Equal(3_000m, PlanChangeCredit.Compute(membership, [], 0, 120_000m, Day0.AddDays(29)).Calculated);

        // Sin pagos no hay crédito.
        Assert.Equal(0m, PlanChangeCredit.Compute(membership, [], 0, 0m, Day0.AddDays(15)).Calculated);

        // El ajuste del staff no puede pasar de la base y exige motivo.
        Assert.Throws<ValidationDomainException>(() => half.Resolve(95_000m, "Cortesía"));
        Assert.Throws<ValidationDomainException>(() => half.Resolve(10_000m, null));
        Assert.Equal((10_000m, "Cortesía"), half.Resolve(10_000m, " Cortesía "));
        Assert.Equal((45_000m, (string?)null), half.Resolve(null, null));
    }

    [Fact]
    public void The_pure_credit_requires_a_membership_covering_today()
    {
        var membership = Membership.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Day0, 30, pricing: new MembershipPricing(90_000m, null, 0m, null));

        Assert.Throws<ConflictException>(() => PlanChangeCredit.Compute(membership, [], 0, 90_000m, Day0.AddDays(40)));
        Assert.Throws<ConflictException>(() => PlanChangeCredit.Compute(membership, [], 0, 90_000m, Day0.AddDays(-1)));
    }

    // ---- casos de uso ----

    [Fact]
    public async Task Changing_plan_cancels_the_current_one_and_creates_the_new_one_with_the_prorated_credit()
    {
        var w = await SeedAsync();
        Guid oldId, paymentId, newPlanId;
        using (var s = As(w, Day0))
        {
            var cheap = await CreatePlanAsync(s, 90_000m);
            newPlanId = await CreatePlanAsync(s, 120_000m);
            oldId = (await Assign(s, w, cheap)).Id;
            paymentId = (await PayAsync(s, w, 90_000m, oldId)).Id;
        }

        // Día 10: quedan 20 de 30 días -> 90.000 × 20 / 30 = 60.000.
        using (var s = As(w, Day0.AddDays(10)))
        {
            var result = await s.ServiceProvider.GetRequiredService<ChangePlanHandler>()
                .HandleAsync(w.MemberId, oldId, new ChangePlanRequest(newPlanId), default);

            Assert.Equal(60_000m, result.CreditApplied);
            Assert.Equal(60_000m, result.TotalToCharge);
            Assert.Equal(0m, result.RefundAmount);

            Assert.Equal("Cancelled", result.Previous.Status);
            Assert.Equal("Cambio de plan", result.Previous.CancellationReason);

            Assert.Equal(Day0.AddDays(10), result.Current.StartDate);
            Assert.Equal(Day0.AddDays(39), result.Current.EndDate);
            Assert.Equal(120_000m, result.Current.AgreedPrice);
            Assert.Equal(60_000m, result.Current.CreditApplied);
            Assert.Equal(60_000m, result.Current.TotalToCharge);
            Assert.Equal(oldId, result.Current.ReplacesMembershipId);
            Assert.Equal(0m, result.Current.EnrollmentFee);
        }

        // Los pagos de la vigente no se anulan: siguen siendo lo que se cobró.
        using (var s = As(w, Day0.AddDays(10)))
        {
            var reversed = await s.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Payment>()
                .AnyAsync(p => p.ReversesPaymentId == paymentId);
            Assert.False(reversed);
            Assert.Equal(90_000m, await s.ServiceProvider.GetRequiredService<IPaymentReader>().GetNetPaidForMembershipAsync(oldId));
        }
    }

    [Fact]
    public async Task The_preview_matches_what_the_change_creates_and_writes_nothing()
    {
        var w = await SeedAsync();
        Guid oldId, newPlanId;
        using (var s = As(w, Day0))
        {
            var cheap = await CreatePlanAsync(s, 90_000m);
            newPlanId = await CreatePlanAsync(s, 120_000m);
            oldId = (await Assign(s, w, cheap)).Id;
            await PayAsync(s, w, 90_000m, oldId);
        }

        using var scope = As(w, Day0.AddDays(10));
        var sp = scope.ServiceProvider;
        var request = new ChangePlanRequest(newPlanId);
        var preview = await sp.GetRequiredService<PreviewPlanChangeHandler>().HandleAsync(w.MemberId, oldId, request, default);

        Assert.Equal(90_000m, preview.AmountPaid);
        Assert.Equal(90_000m, preview.CreditBase);
        Assert.Equal(60_000m, preview.CalculatedCredit);
        Assert.Equal(60_000m, preview.Credit);
        Assert.Equal(20, preview.DaysLeft);
        Assert.Equal(30, preview.DaysTotal);
        Assert.Null(preview.EntriesLeft);
        Assert.Equal(120_000m, preview.AgreedPrice);
        Assert.Equal(60_000m, preview.TotalToCharge);
        Assert.Equal(Day0.AddDays(10), preview.StartDate);
        Assert.Equal(Day0.AddDays(39), preview.EndDate);

        // Nada se escribió: la vigente sigue activa y no hay otra membresía.
        var all = await sp.GetRequiredService<ListMemberMembershipsHandler>().HandleAsync(w.MemberId, default);
        Assert.Single(all);
        Assert.Equal("Active", all[0].Status);

        var result = await sp.GetRequiredService<ChangePlanHandler>().HandleAsync(w.MemberId, oldId, request, default);
        Assert.Equal(preview.TotalToCharge, result.TotalToCharge);
        Assert.Equal(preview.CreditApplied, result.CreditApplied);
        Assert.Equal(preview.EndDate, result.Current.EndDate);
    }

    [Fact]
    public async Task A_credit_above_the_new_price_is_refunded_with_a_negative_payment_and_charges_nothing()
    {
        var w = await SeedAsync();
        Guid oldId, cheapPlanId;
        using (var s = As(w, Day0))
        {
            var expensive = await CreatePlanAsync(s, 300_000m);
            cheapPlanId = await CreatePlanAsync(s, 50_000m);
            oldId = (await Assign(s, w, expensive)).Id;
            await PayAsync(s, w, 300_000m, oldId);
        }

        // Día 10: crédito 300.000 × 20 / 30 = 200.000; el plan nuevo cuesta 50.000 -> se devuelven 150.000.
        using var scope = As(w, Day0.AddDays(10));
        var sp = scope.ServiceProvider;
        var preview = await sp.GetRequiredService<PreviewPlanChangeHandler>()
            .HandleAsync(w.MemberId, oldId, new ChangePlanRequest(cheapPlanId), default);
        Assert.Equal(200_000m, preview.Credit);
        Assert.Equal(50_000m, preview.CreditApplied);
        Assert.Equal(0m, preview.TotalToCharge);
        Assert.Equal(150_000m, preview.RefundAmount);

        var result = await sp.GetRequiredService<ChangePlanHandler>()
            .HandleAsync(w.MemberId, oldId, new ChangePlanRequest(cheapPlanId), default);
        Assert.Equal(50_000m, result.Current.CreditApplied);
        Assert.Equal(0m, result.Current.TotalToCharge);
        Assert.Equal(150_000m, result.RefundAmount);

        // La devolución es un pago negativo (la UI lo registra) ligado a la membresía anterior.
        var refund = await PayAsync(sp, w, -150_000m, oldId, "Devolución por cambio de plan");
        Assert.Equal(-150_000m, refund.Amount);
        Assert.Equal(150_000m, await sp.GetRequiredService<IPaymentReader>().GetNetPaidForMembershipAsync(oldId));
    }

    [Fact]
    public async Task A_pass_credit_is_the_paid_amount_times_the_entries_left_over_the_total()
    {
        var w = await SeedAsync();
        Guid passId, timePlanId;
        using (var s = As(w, Day0))
        {
            var passPlan = (await s.ServiceProvider.GetRequiredService<CreatePlanHandler>().HandleAsync(
                new CreatePlanRequest($"Tiquetera {Guid.NewGuid():N}"[..20], null, 100_000m, 30, null, Kind: "pass", Entries: 10, ValidityDays: 30), default)).Id;
            timePlanId = await CreatePlanAsync(s, 120_000m);
            passId = (await Assign(s, w, passPlan)).Id;
            await PayAsync(s, w, 100_000m, passId);

            // 4 entradas consumidas.
            var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
            for (var i = 0; i < 4; i++)
            {
                db.Add(PassUsage.Create(w.GymId, passId, Guid.NewGuid(), Day0));
            }

            await db.SaveChangesAsync();
        }

        using var scope = As(w, Day0.AddDays(3));
        var preview = await scope.ServiceProvider.GetRequiredService<PreviewPlanChangeHandler>()
            .HandleAsync(w.MemberId, passId, new ChangePlanRequest(timePlanId), default);

        Assert.Equal(6, preview.EntriesLeft);
        Assert.Equal(10, preview.EntriesTotal);
        Assert.Null(preview.DaysLeft);
        Assert.Equal(60_000m, preview.Credit);
        Assert.Equal(60_000m, preview.TotalToCharge);
    }

    [Fact]
    public async Task The_staff_can_adjust_the_credit_with_a_reason_within_what_was_paid()
    {
        var w = await SeedAsync();
        Guid oldId, newPlanId;
        using (var s = As(w, Day0))
        {
            var cheap = await CreatePlanAsync(s, 90_000m);
            newPlanId = await CreatePlanAsync(s, 120_000m);
            oldId = (await Assign(s, w, cheap)).Id;
            await PayAsync(s, w, 90_000m, oldId);
        }

        using var scope = As(w, Day0.AddDays(10));
        var change = scope.ServiceProvider.GetRequiredService<ChangePlanHandler>();

        // Sin motivo, o por encima de lo pagado: 422.
        await Assert.ThrowsAsync<ValidationDomainException>(() => change.HandleAsync(
            w.MemberId, oldId, new ChangePlanRequest(newPlanId, CreditOverride: 70_000m), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() => change.HandleAsync(
            w.MemberId, oldId, new ChangePlanRequest(newPlanId, CreditOverride: 95_000m, CreditAdjustmentReason: "Cortesía"), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() => change.HandleAsync(
            w.MemberId, oldId, new ChangePlanRequest(newPlanId, CreditOverride: -1m, CreditAdjustmentReason: "Cortesía"), default));

        var result = await change.HandleAsync(
            w.MemberId, oldId, new ChangePlanRequest(newPlanId, CreditOverride: 70_000m, CreditAdjustmentReason: "Compensación por una máquina dañada"), default);

        Assert.Equal(70_000m, result.CreditApplied);
        Assert.Equal(50_000m, result.TotalToCharge);
        Assert.Equal("Compensación por una máquina dañada", result.Current.CreditAdjustmentReason);
    }

    [Fact]
    public async Task A_voided_payment_does_not_count_as_paid()
    {
        var w = await SeedAsync();
        Guid oldId, newPlanId;
        using (var s = As(w, Day0))
        {
            var cheap = await CreatePlanAsync(s, 90_000m);
            newPlanId = await CreatePlanAsync(s, 120_000m);
            oldId = (await Assign(s, w, cheap)).Id;
            var payment = await PayAsync(s, w, 90_000m, oldId);
            await s.ServiceProvider.GetRequiredService<VoidPaymentHandler>()
                .HandleAsync(payment.Id, new VoidPaymentRequest("Error de digitación"), default);
        }

        using var scope = As(w, Day0.AddDays(10));
        var preview = await scope.ServiceProvider.GetRequiredService<PreviewPlanChangeHandler>()
            .HandleAsync(w.MemberId, oldId, new ChangePlanRequest(newPlanId), default);

        Assert.Equal(0m, preview.AmountPaid);
        Assert.Equal(0m, preview.Credit);
        Assert.Equal(120_000m, preview.TotalToCharge);
    }

    [Fact]
    public async Task The_new_price_can_carry_a_discount_but_never_an_enrollment_fee()
    {
        var w = await SeedAsync();
        Guid oldId, newPlanId;
        using (var s = As(w, Day0))
        {
            // El gym cobra matrícula, pero un cambio de plan nunca la suma (el miembro ya tiene historial).
            await s.ServiceProvider.GetRequiredService<UpdateMembershipSettingsHandler>()
                .HandleAsync(new UpdateMembershipSettingsRequest(3, 7, 30_000m), default);
            var cheap = await CreatePlanAsync(s, 90_000m);
            newPlanId = await CreatePlanAsync(s, 120_000m);
            var assigned = await Assign(s, w, cheap);
            Assert.Equal(30_000m, assigned.EnrollmentFee);
            oldId = assigned.Id;
            await PayAsync(s, w, 120_000m, oldId);
        }

        using var scope = As(w, Day0.AddDays(10));
        var change = scope.ServiceProvider.GetRequiredService<ChangePlanHandler>();
        await Assert.ThrowsAsync<ValidationDomainException>(() => change.HandleAsync(
            w.MemberId, oldId, new ChangePlanRequest(newPlanId, AgreedPrice: 100_000m), default));

        var result = await change.HandleAsync(
            w.MemberId, oldId, new ChangePlanRequest(newPlanId, AgreedPrice: 100_000m, DiscountReason: "Convenio"), default);

        // La base excluye la matrícula: 90.000 del plan × 20 / 30 = 60.000.
        Assert.Equal(60_000m, result.CreditApplied);
        Assert.Equal(0m, result.Current.EnrollmentFee);
        Assert.Equal(100_000m, result.Current.AgreedPrice);
        Assert.Equal(40_000m, result.TotalToCharge);
        Assert.Equal("Convenio", result.Current.DiscountReason);
    }

    [Fact]
    public async Task A_second_change_keeps_the_credit_that_the_first_one_brought()
    {
        var w = await SeedAsync();
        Guid oldId, bId, cId;
        using (var s = As(w, Day0))
        {
            var a = await CreatePlanAsync(s, 90_000m);
            bId = await CreatePlanAsync(s, 120_000m);
            cId = await CreatePlanAsync(s, 150_000m);
            oldId = (await Assign(s, w, a)).Id;
            await PayAsync(s, w, 90_000m, oldId);
        }

        Guid second;
        using (var s = As(w, Day0.AddDays(10)))
        {
            var first = await s.ServiceProvider.GetRequiredService<ChangePlanHandler>()
                .HandleAsync(w.MemberId, oldId, new ChangePlanRequest(bId), default);
            await PayAsync(s, w, first.TotalToCharge, first.Current.Id);   // 60.000 en efectivo + 60.000 de crédito
            second = first.Current.Id;
        }

        // Mismo día, sin usar nada: la base de B es lo pagado (60.000) más el crédito que trajo (60.000) = 120.000.
        using (var s = As(w, Day0.AddDays(10)))
        {
            var preview = await s.ServiceProvider.GetRequiredService<PreviewPlanChangeHandler>()
                .HandleAsync(w.MemberId, second, new ChangePlanRequest(cId), default);
            Assert.Equal(60_000m, preview.AmountPaid);
            Assert.Equal(120_000m, preview.CreditBase);
            Assert.Equal(120_000m, preview.Credit);
            Assert.Equal(30_000m, preview.TotalToCharge);
        }
    }

    [Fact]
    public async Task A_scheduled_freeze_does_not_dilute_the_credit()
    {
        var w = await SeedAsync();
        Guid oldId, newPlanId;
        using (var s = As(w, Day0))
        {
            var planWithFreeze = (await s.ServiceProvider.GetRequiredService<CreatePlanHandler>().HandleAsync(
                new CreatePlanRequest($"Mensual {Guid.NewGuid():N}"[..20], null, 90_000m, 30, null, 10, 1), default)).Id;
            newPlanId = await CreatePlanAsync(s, 120_000m);
            oldId = (await Assign(s, w, planWithFreeze)).Id;
            await PayAsync(s, w, 90_000m, oldId);
            await s.ServiceProvider.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                w.MemberId, oldId, new CreateFreezeRequest(Day0.AddDays(5), Day0.AddDays(9), "Viaje"), default);
        }

        // El fin se corrió 5 días (35 días de calendario, 30 de servicio): hoy mismo, el crédito es el 100 % de lo pagado.
        using var scope = As(w, Day0);
        var preview = await scope.ServiceProvider.GetRequiredService<PreviewPlanChangeHandler>()
            .HandleAsync(w.MemberId, oldId, new ChangePlanRequest(newPlanId), default);
        Assert.Equal(30, preview.DaysTotal);
        Assert.Equal(30, preview.DaysLeft);
        Assert.Equal(90_000m, preview.Credit);
    }

    [Fact]
    public async Task Only_the_current_membership_can_change_and_not_with_programmed_ones_behind_it()
    {
        var w = await SeedAsync();
        Guid firstId, secondId, planId;
        using (var s = As(w, Day0))
        {
            planId = await CreatePlanAsync(s, 90_000m);
            firstId = (await Assign(s, w, planId)).Id;
            secondId = (await Assign(s, w, planId)).Id;   // encadenada: empieza al terminar la primera
        }

        using (var s = As(w, Day0.AddDays(5)))
        {
            var change = s.ServiceProvider.GetRequiredService<ChangePlanHandler>();

            // La programada no está vigente hoy.
            await Assert.ThrowsAsync<ConflictException>(() => change.HandleAsync(
                w.MemberId, secondId, new ChangePlanRequest(planId), default));

            // La vigente tiene una encadenada detrás: se pide resolverla antes.
            await Assert.ThrowsAsync<ConflictException>(() => change.HandleAsync(
                w.MemberId, firstId, new ChangePlanRequest(planId), default));

            await s.ServiceProvider.GetRequiredService<CancelMembershipHandler>()
                .HandleAsync(w.MemberId, secondId, new CancelMembershipRequest("Ya no la quiere"), default);
            var ok = await change.HandleAsync(w.MemberId, firstId, new ChangePlanRequest(planId), default);
            Assert.Equal("Cancelled", ok.Previous.Status);

            // La que acaba de cancelarse ya no es vigente, ni existe en otro miembro.
            await Assert.ThrowsAsync<ConflictException>(() => change.HandleAsync(
                w.MemberId, firstId, new ChangePlanRequest(planId), default));
            await Assert.ThrowsAsync<NotFoundException>(() => change.HandleAsync(
                Guid.NewGuid(), ok.Current.Id, new ChangePlanRequest(planId), default));
        }

        // Vencida: tampoco.
        using (var s = As(w, Day0.AddDays(400)))
        {
            var all = await s.ServiceProvider.GetRequiredService<ListMemberMembershipsHandler>().HandleAsync(w.MemberId, default);
            var expired = all.First(m => m.Status == "Expired");
            await Assert.ThrowsAsync<ConflictException>(() => s.ServiceProvider.GetRequiredService<ChangePlanHandler>()
                .HandleAsync(w.MemberId, expired.Id, new ChangePlanRequest(planId), default));
        }
    }

    [Fact]
    public async Task An_inactive_plan_cannot_be_the_target()
    {
        var w = await SeedAsync();
        Guid oldId, inactiveId;
        using (var s = As(w, Day0))
        {
            var plan = await CreatePlanAsync(s, 90_000m);
            inactiveId = await CreatePlanAsync(s, 50_000m);
            oldId = (await Assign(s, w, plan)).Id;
            await s.ServiceProvider.GetRequiredService<DeletePlanHandler>().HandleAsync(inactiveId, default);
        }

        using var scope = As(w, Day0.AddDays(2));
        await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<ChangePlanHandler>()
            .HandleAsync(w.MemberId, oldId, new ChangePlanRequest(inactiveId), default));
    }

    // ---- soporte ----

    private IServiceScope As(World w, DateOnly day)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
        return scope;
    }

    private static Task<MembershipResponse> Assign(IServiceScope s, World w, Guid planId)
        => s.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.MemberId, new AssignMembershipRequest(planId, null), default);

    private static async Task<Guid> CreatePlanAsync(IServiceScope s, decimal price)
        => (await s.ServiceProvider.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest($"Plan {Guid.NewGuid():N}"[..20], null, price, 30), default)).Id;

    private static Task<PaymentResponse> PayAsync(
        IServiceScope s, World w, decimal amount, Guid membershipId, string concept = "Membresía")
        => PayAsync(s.ServiceProvider, w, amount, membershipId, concept);

    private static Task<PaymentResponse> PayAsync(
        IServiceProvider sp, World w, decimal amount, Guid membershipId, string concept = "Membresía")
        => sp.GetRequiredService<RecordPaymentHandler>().HandleAsync(
            new RecordPaymentRequest(amount, PaymentMethod.BankTransfer, concept, null, w.MemberId, membershipId), default);

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Plan Change Gym", $"pc-{Guid.NewGuid():N}"[..16]);
        var member = Member.Create(gym.Id, "Ana", "Cambio", $"doc-{Guid.NewGuid():N}"[..12], Day0.AddDays(-100));
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), member);
        await db.SaveChangesAsync();
        return new World(gym.Id, Guid.CreateVersion7(), member.Id);
    }
}
