using GSM.Application.Dashboard;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;
using AttMethod = GSM.Domain.Attendance.AttendanceMethod;

namespace GSM.IntegrationTests;

public sealed class DashboardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Dashboard_aggregates_across_modules()
    {
        var day = new DateOnly(2026, 6, 15);
        var (gymId, staffId) = await SeedAsync(day);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);

        var dash = await scope.ServiceProvider.GetRequiredService<GetDashboardHandler>()
            .HandleAsync(default);

        Assert.Equal(2, dash.ActiveMembers);          // 2 activos, 1 inactivo
        Assert.Equal(1, dash.CheckInsToday);
        Assert.Equal(120_000m, dash.RevenueThisMonth); // 80k + 40k en junio
        Assert.Equal(50_000m, dash.RevenueLastMonth);  // 50k en mayo
        Assert.Equal(1, dash.ExpiredMemberships);       // una venció hace días
        Assert.Equal(1, dash.ExpiringSoon);             // una vence en 3 días
        Assert.Single(dash.ExpiringMemberships);
    }

    private async Task<(Guid GymId, Guid StaffId)> SeedAsync(DateOnly today)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        clock.PinToBusinessDay(today);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships, payments.payments, attendance.attendances, achievements.achievements, achievements.member_achievements restart identity cascade;");

        var gym = Gym.Create("Dash Gym", $"dash-{Guid.NewGuid():N}"[..18]);
        var staff = Staff(gym.Id);
        gym.AssignOwner(staff.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);

        var active1 = Member.Create(gym.Id, "A", "One", "d1", today);
        var active2 = Member.Create(gym.Id, "A", "Two", "d2", today);
        var inactive = Member.Create(gym.Id, "I", "Three", "d3", today);
        inactive.Deactivate();
        db.Add(active1);
        db.Add(active2);
        db.Add(inactive);

        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 80_000m, 30);
        db.Add(plan);
        // Vencida: terminó hace 5 días.
        db.Add(Membership.Create(gym.Id, active1.Id, plan.Id, today.AddDays(-34), 30));
        // Por vencer: termina en 3 días.
        db.Add(Membership.Create(gym.Id, active2.Id, plan.Id, today.AddDays(-26), 30));

        db.Add(AttendanceEntity.Create(gym.Id, active1.Id, AttMethod.Manual, clock.UtcNow, today));

        var utcNow = clock.UtcNow;
        db.Add(Payment.Create(gym.Id, active1.Id, null, 80_000m, PaymentMethod.Cash, "Junio", null, staff.Id, utcNow));
        db.Add(Payment.Create(gym.Id, active2.Id, null, 40_000m, PaymentMethod.Transfer, "Junio", null, staff.Id, utcNow));

        var may = clock.BusinessDayRange(new DateOnly(2026, 5, 20)).Start.AddHours(10);
        db.Add(Payment.Create(gym.Id, active1.Id, null, 50_000m, PaymentMethod.Cash, "Mayo", null, staff.Id, may));

        await db.SaveChangesAsync();
        return (gym.Id, staff.Id);
    }

    private static GSM.Infrastructure.Identity.AppUser Staff(Guid gymId) => new()
    {
        UserName = "staff@dash.local",
        Email = "staff@dash.local",
        NormalizedUserName = "STAFF@DASH.LOCAL",
        NormalizedEmail = "STAFF@DASH.LOCAL",
        EmailConfirmed = true,
        FullName = "Dash Staff",
        IsActive = true,
        SecurityStamp = Guid.NewGuid().ToString(),
    };
}
