using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Payments;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;
using AttendanceMethodEnum = GSM.Domain.Attendance.AttendanceMethod;

namespace GSM.IntegrationTests;

/// <summary>
/// Etapa 11.4: el panel de SuperAdmin. Cartera por estado efectivo, ingresos contratados vs
/// cobrados (por día de negocio) y uso por gym.
/// </summary>
public sealed class AdminDashboardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly LastDayOfSeptember = new(2026, 9, 30);

    [Fact]
    public async Task Portfolio_counts_match_effective_status_and_contracted_excludes_suspended_and_disabled()
    {
        var w = await SeedAsync();

        using var scope = Admin();
        var dash = await scope.ServiceProvider.GetRequiredService<GetAdminDashboardHandler>().HandleAsync(default);

        Assert.Equal(1, dash.Portfolio.CountByStatus["Active"]);
        Assert.Equal(1, dash.Portfolio.CountByStatus["Disabled"]);
        Assert.Equal(1, dash.Portfolio.CountByStatus["Suspended"]);

        // Contratado: solo el gym activo (100.000). El disabled y el suspended no cuentan.
        Assert.Equal(100_000m, dash.Revenue.Contracted);
        Assert.Equal(3, dash.Platform.GymCount);
    }

    [Fact]
    public async Task Collected_revenue_groups_by_business_day_for_a_late_night_payment_on_the_last_day()
    {
        var w = await SeedAsync();

        // 30 de septiembre, 22:00 en Bogotá → ya es 1 de octubre en UTC.
        using (var scope = Admin())
        {
            var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
            clock.PinTo(clock.BusinessDayRange(LastDayOfSeptember).Start.AddHours(22), LastDayOfSeptember);

            await scope.ServiceProvider.GetRequiredService<RegisterSubscriptionPaymentHandler>()
                .HandleAsync(w.ActiveGym, new RegisterSubscriptionPaymentRequest(
                    250_000m, PaymentMethod.BankTransfer, null, null, null, null), default);
        }

        using (var scope = Admin())
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(LastDayOfSeptember);
            var dash = await scope.ServiceProvider.GetRequiredService<GetAdminDashboardHandler>().HandleAsync(default);
            Assert.Equal(250_000m, dash.Revenue.CollectedThisMonth);
        }

        using (var scope = Admin())
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(new DateOnly(2026, 10, 15));
            var dash = await scope.ServiceProvider.GetRequiredService<GetAdminDashboardHandler>().HandleAsync(default);
            Assert.Equal(0m, dash.Revenue.CollectedThisMonth);
            Assert.Equal(250_000m, dash.Revenue.CollectedLastMonth);
        }
    }

    [Fact]
    public async Task Gym_usage_reports_members_checkins_and_last_activity()
    {
        var w = await SeedAsync();

        using var scope = Admin();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(LastDayOfSeptember);
        var dash = await scope.ServiceProvider.GetRequiredService<GetAdminDashboardHandler>().HandleAsync(default);

        var active = dash.GymUsage.Single(u => u.GymId == w.ActiveGym);
        Assert.Equal(1, active.ActiveMembers);
        Assert.Equal(1, active.CheckInsThisMonth);
        Assert.Equal(50_000m, active.MemberRevenueThisMonth);   // el de las 22:00 del 30-sep sí; el de la 01:00 del 1-oct, no
        Assert.Equal(new DateOnly(2026, 9, 20), active.LastCheckIn);

        var disabled = dash.GymUsage.Single(u => u.GymId == w.DisabledGym);
        Assert.Equal(2, disabled.ActiveMembers);                // la baja no cuenta
        Assert.Equal(2, disabled.CheckInsThisMonth);            // personas distintas (A1b-6); el de agosto no cuenta
        Assert.Equal(20_000m, disabled.MemberRevenueThisMonth);
        Assert.Equal(new DateOnly(2026, 9, 11), disabled.LastCheckIn);

        var suspended = dash.GymUsage.Single(u => u.GymId == w.SuspendedGym);
        Assert.Equal(0, suspended.ActiveMembers);
        Assert.Equal(0, suspended.CheckInsThisMonth);
        Assert.Equal(0m, suspended.MemberRevenueThisMonth);
        Assert.Null(suspended.LastCheckIn);

        Assert.Equal(3, dash.Platform.ActiveMembers);
        Assert.Equal(3, dash.Platform.CheckInsThisMonth);        // 1 + 2 personas (A1b-6)
    }

    private IServiceScope Admin()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(LastDayOfSeptember);
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(Guid.CreateVersion7(), gymId: null, UserRole.SuperAdmin);
        return scope;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(LastDayOfSeptember);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, " +
            "attendance.attendances, payments.payments, superadmin.gym_subscriptions, superadmin.subscription_payments restart identity cascade;");

        var active = MakeGym(db, "dash-active", GymStatus.Active, LastDayOfSeptember.AddDays(20), price: 100_000m);
        var disabled = MakeGym(db, "dash-disabled", GymStatus.Active, LastDayOfSeptember.AddDays(-30), price: 300_000m);
        var suspended = MakeGym(db, "dash-suspended", GymStatus.Suspended, LastDayOfSeptember.AddDays(20), price: 500_000m);

        // Un miembro activo con un check-in el 20 de septiembre en el gym activo.
        var member = Member.Create(active, "Ana", "Uso", $"doc-{Guid.NewGuid():N}"[..12], new DateOnly(2026, 1, 1));
        db.Add(member);
        db.Add(AttendanceEntity.Create(
            active, member.Id, AttendanceMethodEnum.Manual,
            new DateTimeOffset(2026, 9, 20, 15, 0, 0, TimeSpan.Zero), new DateOnly(2026, 9, 20)));

        // Cifras distintas en el gym inhabilitado: si la agrupación por gym se cruzara o devolviera el
        // total de la plataforma, el test lo delataría.
        var beto = Member.Create(disabled, "Beto", "Uso", $"doc-{Guid.NewGuid():N}"[..12], new DateOnly(2026, 1, 1));
        var carla = Member.Create(disabled, "Carla", "Uso", $"doc-{Guid.NewGuid():N}"[..12], new DateOnly(2026, 1, 1));
        var baja = Member.Create(disabled, "Dani", "Baja", $"doc-{Guid.NewGuid():N}"[..12], new DateOnly(2026, 1, 1));
        baja.Deactivate();
        db.AddRange(beto, carla, baja);
        foreach (var (who, day) in new[] { (beto, 10), (beto, 11), (carla, 10) })
        {
            db.Add(AttendanceEntity.Create(
                disabled, who.Id, AttendanceMethodEnum.Manual,
                new DateTimeOffset(2026, 9, day, 15, 0, 0, TimeSpan.Zero), new DateOnly(2026, 9, day)));
        }

        // Agosto: fuera del mes del panel.
        db.Add(AttendanceEntity.Create(
            disabled, carla.Id, AttendanceMethodEnum.Manual,
            new DateTimeOffset(2026, 8, 31, 15, 0, 0, TimeSpan.Zero), new DateOnly(2026, 8, 31)));

        // Pagos de miembros en el borde del mes de negocio (Colombia, UTC-5): las 22:00 del 30-sep
        // locales son las 03:00 UTC del 1-oct y cuentan en septiembre; la 01:00 del 1-oct, en octubre.
        var staff = Guid.CreateVersion7();
        db.Add(Payment.Create(active, member.Id, null, 50_000m, PaymentMethod.Cash, "Mensualidad", null, staff,
            new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero)));
        db.Add(Payment.Create(active, member.Id, null, 70_000m, PaymentMethod.Cash, "Mensualidad", null, staff,
            new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero)));
        db.Add(Payment.Create(disabled, beto.Id, null, 20_000m, PaymentMethod.BankTransfer, "Mensualidad", null, staff,
            new DateTimeOffset(2026, 9, 15, 15, 0, 0, TimeSpan.Zero)));

        await db.SaveChangesAsync();
        return new World(active, disabled, suspended);
    }

    private static Guid MakeGym(GsmDbContext db, string slug, GymStatus status, DateOnly paidThrough, decimal price)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create(slug, $"{slug}-{suffix}");
        if (status != GymStatus.Trial)
        {
            gym.SetCommercialStatus(status);
        }

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(GymSubscription.Create(gym.Id, price, LastDayOfSeptember.AddDays(-60), paidThrough));
        return gym.Id;
    }

    private sealed record World(Guid ActiveGym, Guid DisabledGym, Guid SuspendedGym);
}
