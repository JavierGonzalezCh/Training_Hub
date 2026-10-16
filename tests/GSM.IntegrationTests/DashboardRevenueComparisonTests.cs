using GSM.Application.Dashboard;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// B2: el comparativo de ingresos del dashboard compara el mes en curso contra los MISMOS días del mes anterior
/// (del 1 al día de hoy), no contra el mes anterior completo. El 7 de octubre mostraba "−81 %".
/// </summary>
public sealed class DashboardRevenueComparisonTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task On_the_seventh_the_comparison_uses_september_first_to_seventh_only()
    {
        var today = new DateOnly(2026, 10, 7);
        var (gymId, staffId) = await SeedAsync(
            (new DateOnly(2026, 9, 3), 10, 30_000m),   // dentro
            (new DateOnly(2026, 9, 7), 20, 5_000m),    // 20:00 local del 7 = 01:00 UTC del 8: sigue siendo del día 7
            (new DateOnly(2026, 9, 8), 0, 99_000m),    // 00:00 del 8: fuera
            (new DateOnly(2026, 9, 20), 10, 100_000m), // fuera
            (new DateOnly(2026, 10, 2), 10, 40_000m)); // mes en curso

        var dash = await DashboardAsync(gymId, staffId, today);

        Assert.Equal(40_000m, dash.Finances!.RevenueThisMonth);
        Assert.Equal(35_000m, dash.Finances.RevenueLastMonth);
    }

    [Fact]
    public async Task When_the_previous_month_is_shorter_the_comparison_runs_to_its_last_day()
    {
        var today = new DateOnly(2026, 3, 31);
        var (gymId, staffId) = await SeedAsync(
            (new DateOnly(2026, 2, 1), 10, 5_000m),
            (new DateOnly(2026, 2, 28), 23, 10_000m), // 23:00 local del 28 de febrero
            (new DateOnly(2026, 3, 1), 0, 70_000m));  // marzo: no cuenta en febrero

        var dash = await DashboardAsync(gymId, staffId, today);

        Assert.Equal(15_000m, dash.Finances!.RevenueLastMonth);
        Assert.Equal(70_000m, dash.Finances.RevenueThisMonth);
    }

    [Fact]
    public async Task On_the_first_of_the_month_only_the_first_day_of_the_previous_month_counts()
    {
        var today = new DateOnly(2026, 6, 1);
        var (gymId, staffId) = await SeedAsync(
            (new DateOnly(2026, 5, 1), 9, 20_000m),
            (new DateOnly(2026, 5, 2), 9, 80_000m));

        var dash = await DashboardAsync(gymId, staffId, today);

        Assert.Equal(20_000m, dash.Finances!.RevenueLastMonth);
    }

    private async Task<DashboardResponse> DashboardAsync(Guid gymId, Guid staffId, DateOnly today)
    {
        using var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(staffId, gymId, UserRole.Staff);
        user.Permissions.Add(StaffPermission.ViewFinances);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);

        return await scope.ServiceProvider.GetRequiredService<GetDashboardHandler>().HandleAsync(default);
    }

    private async Task<(Guid GymId, Guid StaffId)> SeedAsync(params (DateOnly Day, int Hour, decimal Amount)[] payments)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships, payments.payments restart identity cascade;");

        var gym = Gym.Create("Compare Gym", $"cmp-{Guid.NewGuid():N}"[..18]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@cmp.local",
            Email = "staff@cmp.local",
            NormalizedUserName = "STAFF@CMP.LOCAL",
            NormalizedEmail = "STAFF@CMP.LOCAL",
            EmailConfirmed = true,
            FullName = "Compare Staff",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);

        foreach (var (day, hour, amount) in payments)
        {
            var at = clock.BusinessDayRange(day).Start.AddHours(hour);
            db.Add(Payment.Create(gym.Id, null, null, amount, PaymentMethod.Card, "Pago", null, staff.Id, at));
        }

        await db.SaveChangesAsync();
        return (gym.Id, staff.Id);
    }
}
