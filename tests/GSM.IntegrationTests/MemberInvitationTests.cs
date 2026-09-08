using GSM.Application.Common.Abstractions;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class MemberInvitationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Invite_then_register_links_the_member_and_enforces_self_access()
    {
        var (gymId, staffId, target, other) = await SeedAsync();

        // Staff emite la invitación.
        string code;
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
                .ActAs(staffId, gymId, UserRole.Staff);
            var invite = scope.ServiceProvider.GetRequiredService<InviteMemberHandler>();
            code = (await invite.HandleAsync(target, CancellationToken.None)).Code;
        }

        // El miembro canjea el código (anónimo).
        Guid userId;
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var register = scope.ServiceProvider.GetRequiredService<RegisterMemberHandler>();
            var result = await register.HandleAsync(
                new RegisterMemberRequest(code, "doc-target", "target@member.local", "MemberPass_123"),
                CancellationToken.None);
            userId = result.UserId;
            Assert.Equal(target, result.MemberId);
        }

        // El miembro quedó vinculado y con cuenta de rol member en su gym.
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var member = await db.Set<Member>().SingleAsync(m => m.Id == target);
            var user = await db.Users.SingleAsync(u => u.Id == userId);

            Assert.Equal(userId, member.UserId);
            Assert.Equal(UserRole.Member, user.Role);
            Assert.Equal(gymId, user.GymId);
        }

        // Como ese miembro: ve su ficha, no la de otro.
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
                .ActAs(userId, gymId, UserRole.Member);
            var get = scope.ServiceProvider.GetRequiredService<GetMemberHandler>();

            var own = await get.HandleAsync(target, CancellationToken.None);
            Assert.Equal(target, own.Id);

            await Assert.ThrowsAsync<ForbiddenDomainException>(
                () => get.HandleAsync(other, CancellationToken.None));
        }
    }

    private async Task<(Guid GymId, Guid StaffId, Guid Target, Guid Other)> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations restart identity cascade;");

        var today = scope.ServiceProvider.GetRequiredService<IClock>().Today;
        var gym = Gym.Create("Invite Gym", $"invite-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@invite.local",
            Email = "staff@invite.local",
            NormalizedUserName = "STAFF@INVITE.LOCAL",
            NormalizedEmail = "STAFF@INVITE.LOCAL",
            EmailConfirmed = true,
            FullName = "Invite Staff",
            Role = UserRole.Staff,
            GymId = gym.Id,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);

        var target = Member.Create(gym.Id, "Target", "Member", "doc-target", today);
        var other = Member.Create(gym.Id, "Other", "Member", "doc-other", today);

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(target);
        db.Add(other);
        await db.SaveChangesAsync();

        return (gym.Id, staff.Id, target.Id, other.Id);
    }
}
