using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class PaymentVoidTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Voiding_records_the_inverse_entry_today_and_leaves_the_original_day_untouched()
    {
        var (gymId, staffId, memberId) = await SeedAsync();
        var payDay = new DateOnly(2026, 3, 10);
        var voidDay = payDay.AddDays(1);

        PaymentResponse original;
        using (var scope = Staff(gymId, staffId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(payDay);
            original = await scope.ServiceProvider.GetRequiredService<RecordPaymentHandler>()
                .HandleAsync(new RecordPaymentRequest(90_000m, PaymentMethod.Transfer, "Mensualidad", null, memberId, null), default);
        }

        PaymentResponse reversal;
        using (var scope = Staff(gymId, staffId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(voidDay);
            reversal = await scope.ServiceProvider.GetRequiredService<VoidPaymentHandler>()
                .HandleAsync(original.Id, new VoidPaymentRequest("Monto mal digitado"), default);
        }

        Assert.Equal(-90_000m, reversal.Amount);
        Assert.Equal("Transfer", reversal.Method);
        Assert.Equal(memberId, reversal.MemberId);
        Assert.Equal(original.Id, reversal.ReversesPaymentId);
        Assert.Equal("Anulación: Mensualidad", reversal.Concept);
        Assert.Equal("Monto mal digitado", reversal.Notes);

        using (var scope = Staff(gymId, staffId))
        {
            var cash = scope.ServiceProvider.GetRequiredService<GetDailyCashRegisterHandler>();

            var first = await cash.HandleAsync(payDay, default);
            Assert.Equal(90_000m, first.Total);
            Assert.True(Assert.Single(first.Payments).IsReversed);

            var second = await cash.HandleAsync(voidDay, default);
            Assert.Equal(-90_000m, second.Total);
            Assert.False(Assert.Single(second.Payments).IsReversed);

            var fetched = await scope.ServiceProvider.GetRequiredService<GetPaymentHandler>().HandleAsync(original.Id, default);
            Assert.True(fetched.IsReversed);
        }
    }

    [Fact]
    public async Task A_payment_is_voided_once_and_a_reversal_cannot_be_voided()
    {
        var (gymId, staffId, _) = await SeedAsync();

        using var scope = Staff(gymId, staffId);
        var original = await scope.ServiceProvider.GetRequiredService<RecordPaymentHandler>()
            .HandleAsync(new RecordPaymentRequest(30_000m, PaymentMethod.Cash, "Producto", null, null, null), default);
        var handler = scope.ServiceProvider.GetRequiredService<VoidPaymentHandler>();

        var reversal = await handler.HandleAsync(original.Id, new VoidPaymentRequest("Error de cobro"), default);

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.HandleAsync(original.Id, new VoidPaymentRequest("Otra vez"), default));
        await Assert.ThrowsAsync<ValidationDomainException>(
            () => handler.HandleAsync(reversal.Id, new VoidPaymentRequest("Deshacer la anulación"), default));
        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.HandleAsync(Guid.NewGuid(), new VoidPaymentRequest("No existe"), default));
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

        var gym = Gym.Create("Void Gym", $"void-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@void.local",
            Email = "staff@void.local",
            NormalizedUserName = "STAFF@VOID.LOCAL",
            NormalizedEmail = "STAFF@VOID.LOCAL",
            EmailConfirmed = true,
            FullName = "Void Staff",
            Role = UserRole.Staff,
            GymId = gym.Id,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Void", "Member", "doc-void", new DateOnly(2026, 1, 1));

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(member);
        await db.SaveChangesAsync();

        return (gym.Id, staff.Id, member.Id);
    }
}
