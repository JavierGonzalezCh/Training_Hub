using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class CashRegisterTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Daily_close_totals_match_by_method_and_total_and_a_past_close_is_immutable()
    {
        var (gymId, staffId, memberId) = await SeedAsync();
        var day = new DateOnly(2026, 3, 10);

        using (var scope = Staff(gymId, staffId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
            var record = scope.ServiceProvider.GetRequiredService<RecordPaymentHandler>();

            await record.HandleAsync(new RecordPaymentRequest(100_000m, PaymentMethod.Cash, "Mensualidad", null, memberId, null), default);
            await record.HandleAsync(new RecordPaymentRequest(50_000m, PaymentMethod.Transfer, "Mensualidad", null, memberId, null), default);
            await record.HandleAsync(new RecordPaymentRequest(30_000m, PaymentMethod.Cash, "Producto", null, null, null), default);
            await record.HandleAsync(new RecordPaymentRequest(-20_000m, PaymentMethod.Cash, "Ajuste: devolución", null, null, null), default);
        }

        DailyCashRegisterResponse close;
        using (var scope = Staff(gymId, staffId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
            close = await scope.ServiceProvider.GetRequiredService<GetDailyCashRegisterHandler>()
                .HandleAsync(null, default);
        }

        Assert.Equal(day, close.Date);
        Assert.Equal(4, close.TransactionCount);
        Assert.Equal(110_000m, close.TotalsByMethod["Cash"]);   // 100k + 30k - 20k
        Assert.Equal(50_000m, close.TotalsByMethod["Transfer"]);
        Assert.Equal(160_000m, close.Total);
        Assert.Equal(close.Total, close.TotalsByMethod.Values.Sum());

        // Un pago posterior en otro día no altera el cierre ya hecho.
        using (var scope = Staff(gymId, staffId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day.AddDays(2));
            await scope.ServiceProvider.GetRequiredService<RecordPaymentHandler>()
                .HandleAsync(new RecordPaymentRequest(999_000m, PaymentMethod.Card, "Otro día", null, null, null), default);
        }

        using (var scope = Staff(gymId, staffId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
            var again = await scope.ServiceProvider.GetRequiredService<GetDailyCashRegisterHandler>()
                .HandleAsync(day, default);
            Assert.Equal(160_000m, again.Total);
            Assert.Equal(4, again.TransactionCount);
        }
    }

    private IServiceScope Staff(Guid gymId, Guid staffId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        return scope;
    }

    private async Task<(Guid GymId, Guid StaffId, Guid MemberId)> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships, payments.payments restart identity cascade;");

        var gym = Gym.Create("Cash Gym", $"cash-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@cash.local",
            Email = "staff@cash.local",
            NormalizedUserName = "STAFF@CASH.LOCAL",
            NormalizedEmail = "STAFF@CASH.LOCAL",
            EmailConfirmed = true,
            FullName = "Cash Staff",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Cash", "Member", "doc-cash", new DateOnly(2026, 1, 1));

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(member);
        await db.SaveChangesAsync();

        return (gym.Id, staff.Id, member.Id);
    }
}
