using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using GSM.Application.Payments.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// El ingreso mensual es un periodo de negocio, no un rango UTC. Colombia es UTC-5, así que un
/// pago de las últimas cinco horas del último día del mes se almacena con una fecha UTC que ya
/// pertenece al mes siguiente: si el rango se construye en UTC, ese dinero desaparece del mes.
/// </summary>
public sealed class MonthlyRevenueTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Late_night_payment_on_the_last_day_counts_in_its_own_business_month()
    {
        var (gymId, staffId) = await SeedAsync();

        // 30 de septiembre, 20:00 en Bogotá = 1 de octubre, 01:00 UTC.
        var lastDayOfSeptember = new DateOnly(2026, 9, 30);
        using (var scope = Staff(gymId, staffId))
        {
            var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
            clock.PinTo(clock.BusinessDayRange(lastDayOfSeptember).Start.AddHours(20), lastDayOfSeptember);

            await scope.ServiceProvider.GetRequiredService<RecordPaymentHandler>()
                .HandleAsync(
                    new RecordPaymentRequest(999_000m, PaymentMethod.Cash, "Mensualidad de última hora", null, null, null),
                    default);
        }

        // Un pago normal a media mañana del mismo mes, para que el total no sea el del caso límite solo.
        var midSeptember = new DateOnly(2026, 9, 15);
        using (var scope = Staff(gymId, staffId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(midSeptember);
            await scope.ServiceProvider.GetRequiredService<RecordPaymentHandler>()
                .HandleAsync(
                    new RecordPaymentRequest(100_000m, PaymentMethod.Cash, "Mensualidad", null, null, null),
                    default);
        }

        using var reading = Staff(gymId, staffId);
        var reader = reading.ServiceProvider.GetRequiredService<IPaymentReader>();

        var september = await reader.GetMonthlyRevenueAsync(gymId, 2026, 9, default);
        var october = await reader.GetMonthlyRevenueAsync(gymId, 2026, 10, default);

        Assert.Equal(1_099_000m, september);
        Assert.Equal(0m, october);
    }

    private IServiceScope Staff(Guid gymId, Guid staffId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        return scope;
    }

    private async Task<(Guid GymId, Guid StaffId)> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships, payments.payments restart identity cascade;");

        var gym = Gym.Create("Revenue Gym", $"rev-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@rev.local",
            Email = "staff@rev.local",
            NormalizedUserName = "STAFF@REV.LOCAL",
            NormalizedEmail = "STAFF@REV.LOCAL",
            EmailConfirmed = true,
            FullName = "Revenue Staff",
            Role = UserRole.Staff,
            GymId = gym.Id,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        await db.SaveChangesAsync();

        return (gym.Id, staff.Id);
    }
}
