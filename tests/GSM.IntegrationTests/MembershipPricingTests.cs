using GSM.Application.Memberships;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Payments;
using GSM.Infrastructure.Migrations;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>MS2: precio acordado, descuento con motivo, matrícula y total a cobrar al asignar.</summary>
public sealed class MembershipPricingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Default_agreed_price_is_the_plan_price_without_discount_or_enrollment()
    {
        var w = await SeedAsync();
        MembershipResponse membership;
        using (var scope = As(w, UserRole.Staff))
        {
            var planId = await CreatePlanAsync(scope, 80_000m);
            membership = await Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null));
        }

        Assert.Equal(80_000m, membership.AgreedPrice);
        Assert.Null(membership.DiscountReason);
        Assert.Equal(0m, membership.EnrollmentFee);
        Assert.Null(membership.EnrollmentWaivedReason);
        Assert.Equal(80_000m, membership.TotalToCharge);
    }

    [Fact]
    public async Task A_discounted_price_needs_a_reason_and_is_kept_when_the_plan_price_changes()
    {
        var w = await SeedAsync();
        using var scope = As(w, UserRole.Staff);
        var planId = await CreatePlanAsync(scope, 100_000m);

        // Sin motivo, o con un motivo vacío, no se asigna.
        await Assert.ThrowsAsync<ValidationDomainException>(
            () => Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null, AgreedPrice: 70_000m)));
        await Assert.ThrowsAsync<ValidationDomainException>(
            () => Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null, AgreedPrice: 70_000m, DiscountReason: "  ")));

        var membership = await Assign(scope, w.Members[0],
            new AssignMembershipRequest(planId, null, AgreedPrice: 70_000m, DiscountReason: "Convenio con la empresa"));
        Assert.Equal(70_000m, membership.AgreedPrice);
        Assert.Equal("Convenio con la empresa", membership.DiscountReason);
        Assert.Equal(70_000m, membership.TotalToCharge);

        // El plan sube de precio: la membresía conserva lo acordado.
        await scope.ServiceProvider.GetRequiredService<UpdatePlanHandler>()
            .HandleAsync(planId, new UpdatePlanRequest("Mensual", null, 120_000m, 30), default);
        var history = await scope.ServiceProvider.GetRequiredService<IMembershipRepository>().GetHistoryAsync(w.Members[0]);
        Assert.Equal(70_000m, Assert.Single(history).AgreedPrice);
    }

    [Fact]
    public async Task A_price_above_the_plan_or_negative_is_rejected_and_an_equal_price_ignores_the_reason()
    {
        var w = await SeedAsync();
        using var scope = As(w, UserRole.Staff);
        var planId = await CreatePlanAsync(scope, 100_000m);

        await Assert.ThrowsAsync<ValidationDomainException>(
            () => Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null, AgreedPrice: 100_001m)));
        await Assert.ThrowsAsync<ValidationDomainException>(
            () => Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null, AgreedPrice: -1m)));

        var membership = await Assign(scope, w.Members[0],
            new AssignMembershipRequest(planId, null, AgreedPrice: 100_000m, DiscountReason: "no aplica"));
        Assert.Null(membership.DiscountReason);
    }

    [Fact]
    public async Task A_free_membership_needs_the_discount_reason_and_has_nothing_to_charge()
    {
        var w = await SeedAsync();
        using var scope = As(w, UserRole.Staff);
        var planId = await CreatePlanAsync(scope, 100_000m);

        await Assert.ThrowsAsync<ValidationDomainException>(
            () => Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null, AgreedPrice: 0m)));

        var membership = await Assign(scope, w.Members[0],
            new AssignMembershipRequest(planId, null, AgreedPrice: 0m, DiscountReason: "Cortesía del gerente"));
        Assert.Equal(0m, membership.TotalToCharge);
    }

    [Fact]
    public async Task Enrollment_is_added_only_to_the_first_membership_and_can_be_waived_with_a_reason()
    {
        var w = await SeedAsync();
        using var scope = As(w, UserRole.Owner);
        await SetEnrollmentFeeAsync(scope, 30_000m);
        var planId = await CreatePlanAsync(scope, 80_000m);

        // La vista previa anuncia la matrícula antes de confirmar.
        var preview = await scope.ServiceProvider.GetRequiredService<PreviewMembershipHandler>()
            .HandleAsync(w.Members[0], planId, null, null, default);
        Assert.Equal(30_000m, preview.EnrollmentFee);
        Assert.Equal(80_000m, preview.PlanPrice);

        var first = await Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null));
        Assert.Equal(30_000m, first.EnrollmentFee);
        Assert.Equal(110_000m, first.TotalToCharge);

        // La renovación ya no paga matrícula, ni se anuncia en la vista previa.
        var second = await scope.ServiceProvider.GetRequiredService<PreviewMembershipHandler>()
            .HandleAsync(w.Members[0], planId, null, null, default);
        Assert.Equal(0m, second.EnrollmentFee);
        var renewal = await Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null));
        Assert.Equal(0m, renewal.EnrollmentFee);
        Assert.Equal(80_000m, renewal.TotalToCharge);
    }

    [Fact]
    public async Task Waiving_the_enrollment_needs_a_reason_and_records_it()
    {
        var w = await SeedAsync();
        using var scope = As(w, UserRole.Owner);
        await SetEnrollmentFeeAsync(scope, 30_000m);
        var planId = await CreatePlanAsync(scope, 80_000m);

        await Assert.ThrowsAsync<ValidationDomainException>(
            () => Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null, WaiveEnrollment: true)));

        var membership = await Assign(scope, w.Members[0],
            new AssignMembershipRequest(planId, null, WaiveEnrollment: true, EnrollmentWaivedReason: "Promoción de apertura"));
        Assert.Equal(0m, membership.EnrollmentFee);
        Assert.Equal("Promoción de apertura", membership.EnrollmentWaivedReason);
        Assert.Equal(80_000m, membership.TotalToCharge);
    }

    [Fact]
    public async Task A_member_with_only_a_cancelled_membership_does_not_pay_enrollment_again()
    {
        var w = await SeedAsync();
        using var scope = As(w, UserRole.Owner);
        await SetEnrollmentFeeAsync(scope, 30_000m);
        var planId = await CreatePlanAsync(scope, 80_000m);

        var first = await Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null));
        Assert.Equal(30_000m, first.EnrollmentFee);
        await scope.ServiceProvider.GetRequiredService<CancelMembershipHandler>()
            .HandleAsync(w.Members[0], first.Id, new CancelMembershipRequest("Se asignó por error"), default);

        var again = await Assign(scope, w.Members[0], new AssignMembershipRequest(planId, null));
        Assert.Equal(0m, again.EnrollmentFee);
    }

    [Fact]
    public async Task Without_a_configured_fee_there_is_no_enrollment_and_waiving_is_ignored()
    {
        var w = await SeedAsync();
        using var scope = As(w, UserRole.Owner);
        var planId = await CreatePlanAsync(scope, 80_000m);

        var membership = await Assign(scope, w.Members[0],
            new AssignMembershipRequest(planId, null, WaiveEnrollment: true));
        Assert.Equal(0m, membership.EnrollmentFee);
        Assert.Null(membership.EnrollmentWaivedReason);
    }

    [Fact]
    public async Task Enrollment_fee_setting_is_saved_and_validated()
    {
        var w = await SeedAsync();
        using var scope = As(w, UserRole.Owner);

        var defaults = await scope.ServiceProvider.GetRequiredService<GetMembershipSettingsHandler>().HandleAsync(default);
        Assert.Equal(0m, defaults.EnrollmentFee);

        var saved = await scope.ServiceProvider.GetRequiredService<UpdateMembershipSettingsHandler>()
            .HandleAsync(new UpdateMembershipSettingsRequest(3, 7, 45_000.5m), default);
        Assert.Equal(45_000.5m, saved.EnrollmentFee);
        Assert.Equal(45_000.5m,
            (await scope.ServiceProvider.GetRequiredService<GetMembershipSettingsHandler>().HandleAsync(default)).EnrollmentFee);

        await Assert.ThrowsAsync<ValidationDomainException>(
            () => scope.ServiceProvider.GetRequiredService<UpdateMembershipSettingsHandler>()
                .HandleAsync(new UpdateMembershipSettingsRequest(3, 7, -1m), default));
    }

    [Fact]
    public async Task The_migration_script_backfills_agreed_price_from_non_voided_payments_or_the_plan_price()
    {
        var w = await SeedAsync();
        Guid paid, voided, unpaid;
        using (var scope = As(w, UserRole.Owner))
        {
            var planId = await CreatePlanAsync(scope, 100_000m);
            var assign = scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>();
            var pay = scope.ServiceProvider.GetRequiredService<RecordPaymentHandler>();

            paid = (await assign.HandleAsync(w.Members[0], new AssignMembershipRequest(planId, null), default)).Id;
            await pay.HandleAsync(new RecordPaymentRequest(60_000m, PaymentMethod.BankTransfer, "Parte 1", null, w.Members[0], paid), default);
            await pay.HandleAsync(new RecordPaymentRequest(30_000m, PaymentMethod.BankTransfer, "Parte 2", null, w.Members[0], paid), default);

            voided = (await assign.HandleAsync(w.Members[1], new AssignMembershipRequest(planId, null), default)).Id;
            var wrong = await pay.HandleAsync(new RecordPaymentRequest(10_000m, PaymentMethod.BankTransfer, "Mal digitado", null, w.Members[1], voided), default);
            await pay.HandleAsync(new RecordPaymentRequest(100_000m, PaymentMethod.BankTransfer, "Bien", null, w.Members[1], voided), default);
            await scope.ServiceProvider.GetRequiredService<VoidPaymentHandler>()
                .HandleAsync(wrong.Id, new VoidPaymentRequest("Monto mal digitado"), default);

            unpaid = (await assign.HandleAsync(w.Members[2], new AssignMembershipRequest(planId, null), default)).Id;
        }

        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

            // Como quedaban las filas antes de MS2: sin precio acordado.
            await db.Database.ExecuteSqlRawAsync("update memberships.memberships set agreed_price = 0");
            await db.Database.ExecuteSqlRawAsync(MigrationScripts.Read("MS2"));

            var prices = await db.Set<Membership>().IgnoreQueryFilters().AsNoTracking()
                .ToDictionaryAsync(m => m.Id, m => m.AgreedPrice);
            Assert.Equal(90_000m, prices[paid]);     // suma de sus pagos
            Assert.Equal(100_000m, prices[voided]);  // el pago anulado (y su asiento inverso) no cuentan
            Assert.Equal(100_000m, prices[unpaid]);  // sin pagos: el precio del plan
        }
    }

    [Fact]
    public void Pricing_rules_hold_in_the_domain()
    {
        var full = MembershipPricing.Resolve(100m, null, null, 20m, true, false, null);
        Assert.Equal(120m, full.Total);

        var discounted = MembershipPricing.Resolve(100m, 80m, " Amigo ", 20m, true, true, "Promo");
        Assert.Equal("Amigo", discounted.DiscountReason);
        Assert.Equal("Promo", discounted.EnrollmentWaivedReason);
        Assert.Equal(80m, discounted.Total);

        Assert.Throws<ValidationDomainException>(() => MembershipPricing.Resolve(100m, 80m, null, 0m, false, false, null));
        Assert.Throws<ValidationDomainException>(() => MembershipPricing.Resolve(100m, 101m, "x1", 0m, false, false, null));
    }

    private static Task<MembershipResponse> Assign(IServiceScope scope, Guid memberId, AssignMembershipRequest request)
        => scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>().HandleAsync(memberId, request, default);

    private static async Task<Guid> CreatePlanAsync(IServiceScope scope, decimal price)
        => (await scope.ServiceProvider.GetRequiredService<CreatePlanHandler>()
            .HandleAsync(new CreatePlanRequest($"Mensual {Guid.NewGuid():N}"[..16], null, price, 30), default)).Id;

    private static async Task SetEnrollmentFeeAsync(IServiceScope scope, decimal fee)
        => await scope.ServiceProvider.GetRequiredService<UpdateMembershipSettingsHandler>()
            .HandleAsync(new UpdateMembershipSettingsRequest(3, 7, fee), default);

    private IServiceScope As(World w, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, role);
        return scope;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<GSM.Application.Common.Abstractions.IClock>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships, memberships.membership_settings, payments.payments restart identity cascade;");

        var gym = Gym.Create("Pricing Gym", $"price-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@pricing.local",
            Email = "staff@pricing.local",
            NormalizedUserName = "STAFF@PRICING.LOCAL",
            NormalizedEmail = "STAFF@PRICING.LOCAL",
            EmailConfirmed = true,
            FullName = "Pricing Staff",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var members = Enumerable.Range(0, 3)
            .Select(i => Member.Create(gym.Id, "Pricing", $"Member{i}", $"doc-pricing-{i}", clock.Today))
            .ToList();

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.AddRange(members);
        await db.SaveChangesAsync();

        return new World(gym.Id, staff.Id, members.Select(m => m.Id).ToList());
    }

    private sealed record World(Guid GymId, Guid StaffId, List<Guid> Members);
}
