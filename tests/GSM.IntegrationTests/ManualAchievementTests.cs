using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Achievements.Public;
using GSM.Domain.Achievements;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.IntegrationTests;

/// <summary>
/// L4: logros otorgados por el staff (<c>manual</c>): motivo y autor, una vez por miembro, retiro con motivo,
/// nunca se desbloquean solos y no cruzan de gym.
/// </summary>
public sealed class ManualAchievementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    [Fact]
    public async Task Staff_grants_a_manual_achievement_with_reason_and_author_and_the_member_gets_one_notice()
    {
        var w = await SeedAsync();
        var achievementId = await CreateManualAsync(w, "Campeón de abril");

        MemberAchievementItem granted;
        using (var scope = Staff(w))
        {
            granted = await scope.ServiceProvider.GetRequiredService<GrantManualAchievementHandler>().HandleAsync(
                achievementId, new GrantManualAchievementRequest(w.Member, "Ganó la competencia de abril"), default);
        }

        Assert.True(granted.Unlocked);
        Assert.True(granted.IsManual);
        Assert.Equal("Ganó la competencia de abril", granted.GrantReason);

        using (var scope = Staff(w))
        {
            var row = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberAchievement>()
                .SingleAsync(u => u.AchievementId == achievementId);
            Assert.Equal(w.StaffId, row.GrantedBy);
            Assert.Equal("Ganó la competencia de abril", row.GrantReason);
            Assert.Equal(w.Member, row.MemberId);
            Assert.False(row.IsRevoked);

            // La ficha y la vista del miembro lo muestran con el motivo.
            var items = await scope.ServiceProvider.GetRequiredService<GetMemberAchievementsHandler>().HandleAsync(w.Member, default);
            var item = items.Single(i => i.AchievementId == achievementId);
            Assert.True(item.Unlocked);
            Assert.Equal("Ganó la competencia de abril", item.GrantReason);
        }

        Assert.Equal(1, await CountNoticesAsync(w, w.Member));
    }

    [Fact]
    public async Task A_manual_achievement_never_unlocks_by_itself_and_is_never_granted_retroactively()
    {
        var w = await SeedAsync();
        var achievementId = await CreateManualAsync(w, "Solo a mano");

        // El miembro entrena todos los días: ninguna regla automática puede tocar un logro manual.
        await AddDaysAsync(w, w.Member, 12);

        using (var scope = Staff(w))
        {
            var unlocked = await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(w.Member);
            Assert.DoesNotContain(unlocked, a => a.AchievementId == achievementId);

            var preview = await scope.ServiceProvider.GetRequiredService<PreviewRetroactiveAchievementHandler>()
                .HandleAsync(new RetroactivePreviewRequest(AchievementConditionType.Manual, 1, achievementId), default);
            Assert.Equal(0, preview.EligibleCount);
        }

        using (var scope = Owner(w))
        {
            var granted = await scope.ServiceProvider.GetRequiredService<GrantRetroactiveAchievementHandler>()
                .HandleAsync(achievementId, default);
            Assert.Equal(0, granted.GrantedCount);

            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberAchievement>()
                .CountAsync(u => u.AchievementId == achievementId));
            var item = (await scope.ServiceProvider.GetRequiredService<GetMemberAchievementsHandler>()
                .HandleAsync(w.Member, default)).Single(i => i.AchievementId == achievementId);
            Assert.False(item.Unlocked);
        }
    }

    [Fact]
    public async Task Granting_the_same_manual_achievement_twice_is_a_conflict_and_leaves_one_row_and_one_notice()
    {
        var w = await SeedAsync();
        var achievementId = await CreateManualAsync(w, "Una sola vez");
        await GrantAsync(w, achievementId, "Primera vez");

        using (var scope = Staff(w))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<GrantManualAchievementHandler>().HandleAsync(
                    achievementId, new GrantManualAchievementRequest(w.Member, "Otra vez"), default));
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberAchievement>()
                .CountAsync(u => u.AchievementId == achievementId));
        }

        Assert.Equal(1, await CountNoticesAsync(w, w.Member));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ok")]
    public async Task Granting_without_a_real_reason_is_rejected(string reason)
    {
        var w = await SeedAsync();
        var achievementId = await CreateManualAsync(w, "Con motivo");

        using var scope = Staff(w);
        await Assert.ThrowsAsync<ValidationDomainException>(() => scope.ServiceProvider
            .GetRequiredService<GrantManualAchievementHandler>().HandleAsync(
                achievementId, new GrantManualAchievementRequest(w.Member, reason), default));
    }

    [Fact]
    public async Task Only_manual_active_achievements_can_be_granted_by_hand()
    {
        var w = await SeedAsync();
        Guid automatic;
        using (var scope = Owner(w))
        {
            automatic = (await scope.ServiceProvider.GetRequiredService<CreateAchievementHandler>().HandleAsync(
                new CreateAchievementRequest("Automático", null, null, AchievementConditionType.Visits, 5), default)).Id;
        }

        var inactive = await CreateManualAsync(w, "Inactivo");
        using (var scope = Owner(w))
        {
            await scope.ServiceProvider.GetRequiredService<DeleteAchievementHandler>().HandleAsync(inactive, default);
        }

        using (var scope = Staff(w))
        {
            var handler = scope.ServiceProvider.GetRequiredService<GrantManualAchievementHandler>();
            await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(
                automatic, new GrantManualAchievementRequest(w.Member, "No debería"), default));
            await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(
                inactive, new GrantManualAchievementRequest(w.Member, "No debería"), default));
        }
    }

    [Fact]
    public async Task A_manual_achievement_can_be_revoked_with_a_reason_and_granted_again_but_automatic_ones_cannot_be_revoked()
    {
        var w = await SeedAsync();
        var achievementId = await CreateManualAsync(w, "Por error");
        await GrantAsync(w, achievementId, "Me equivoqué de miembro");

        using (var scope = Staff(w))
        {
            var revoke = scope.ServiceProvider.GetRequiredService<RevokeManualAchievementHandler>();
            await Assert.ThrowsAsync<ValidationDomainException>(() => revoke.HandleAsync(
                achievementId, w.Member, new RevokeManualAchievementRequest(" "), default));
            await revoke.HandleAsync(
                achievementId, w.Member, new RevokeManualAchievementRequest("Se otorgó por error"), default);
        }

        using (var scope = Staff(w))
        {
            var row = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberAchievement>()
                .SingleAsync(u => u.AchievementId == achievementId);
            Assert.True(row.IsRevoked);
            Assert.Equal(w.StaffId, row.RevokedBy);
            Assert.Equal("Se otorgó por error", row.RevokeReason);

            // Retirado: ya no cuenta como ganado.
            var item = (await scope.ServiceProvider.GetRequiredService<GetMemberAchievementsHandler>()
                .HandleAsync(w.Member, default)).Single(i => i.AchievementId == achievementId);
            Assert.False(item.Unlocked);

            // Retirarlo otra vez es un conflicto.
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<RevokeManualAchievementHandler>().HandleAsync(
                    achievementId, w.Member, new RevokeManualAchievementRequest("Otra vez"), default));
        }

        // Se puede volver a otorgar: se reabre la misma fila.
        await GrantAsync(w, achievementId, "Ahora sí es el correcto");
        using (var scope = Staff(w))
        {
            var rows = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberAchievement>()
                .Where(u => u.AchievementId == achievementId).ToListAsync();
            var row = Assert.Single(rows);
            Assert.False(row.IsRevoked);
            Assert.Equal("Ahora sí es el correcto", row.GrantReason);
        }

        // Un logro automático ya ganado nunca se retira.
        Guid automatic;
        using (var scope = Owner(w))
        {
            automatic = (await scope.ServiceProvider.GetRequiredService<CreateAchievementHandler>().HandleAsync(
                new CreateAchievementRequest("Primera visita", null, null, AchievementConditionType.Visits, 1), default)).Id;
        }

        await AddDaysAsync(w, w.Member, 1);
        using (var scope = Staff(w))
        {
            await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(w.Member);
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<RevokeManualAchievementHandler>().HandleAsync(
                    automatic, w.Member, new RevokeManualAchievementRequest("Quiero quitarlo"), default));
        }
    }

    [Fact]
    public async Task Another_gym_cannot_grant_or_revoke_and_cannot_grant_to_a_foreign_member()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        var achievementId = await CreateManualAsync(w, "De mi gym");
        await GrantAsync(w, achievementId, "Motivo válido");

        using (var scope = Staff(other))
        {
            // El logro de otro gym no existe para este gym.
            await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
                .GetRequiredService<GrantManualAchievementHandler>().HandleAsync(
                    achievementId, new GrantManualAchievementRequest(other.Member, "Intruso"), default));
            await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
                .GetRequiredService<RevokeManualAchievementHandler>().HandleAsync(
                    achievementId, w.Member, new RevokeManualAchievementRequest("Intruso"), default));
        }

        // Y un logro propio no se otorga a un miembro de otro gym.
        var mine = await CreateManualAsync(other, "Del otro gym");
        using (var scope = Staff(other))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
                .GetRequiredService<GrantManualAchievementHandler>().HandleAsync(
                    mine, new GrantManualAchievementRequest(w.Member, "Miembro ajeno"), default));
        }
    }

    [Fact]
    public async Task A_manual_achievement_ignores_the_condition_value()
    {
        var w = await SeedAsync();
        using var scope = Owner(w);
        var created = await scope.ServiceProvider.GetRequiredService<CreateAchievementHandler>().HandleAsync(
            new CreateAchievementRequest("Sin valor", null, null, AchievementConditionType.Manual, 0), default);
        Assert.Equal(1, created.ConditionValue);
        Assert.Equal("Manual", created.ConditionType);
    }

    // ---- helpers ----

    private IServiceScope Owner(World w) => ScopeAs(w, w.OwnerId, UserRole.Owner);

    private IServiceScope Staff(World w) => ScopeAs(w, w.StaffId, UserRole.Staff);

    private IServiceScope ScopeAs(World w, Guid userId, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<Guid> CreateManualAsync(World w, string name)
    {
        using var scope = Owner(w);
        var created = await scope.ServiceProvider.GetRequiredService<CreateAchievementHandler>().HandleAsync(
            new CreateAchievementRequest(name, null, null, AchievementConditionType.Manual, 1), default);
        return created.Id;
    }

    private async Task GrantAsync(World w, Guid achievementId, string reason)
    {
        using var scope = Staff(w);
        await scope.ServiceProvider.GetRequiredService<GrantManualAchievementHandler>().HandleAsync(
            achievementId, new GrantManualAchievementRequest(w.Member, reason), default);
    }

    private async Task<int> CountNoticesAsync(World w, Guid memberId)
    {
        using var scope = Owner(w);
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .CountAsync(n => n.RecipientMemberId == memberId && n.Type == NotificationType.AchievementUnlocked);
    }

    private async Task AddDaysAsync(World w, Guid memberId, int days)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < days; i++)
        {
            db.Add(AttendanceEntity.Create(
                w.GymId, memberId, GSM.Domain.Attendance.AttendanceMethod.Manual, now, Today.AddDays(-i)));
        }

        await db.SaveChangesAsync();
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Manual Gym", $"mn-{Guid.NewGuid():N}"[..16]);
        var member = Member.Create(gym.Id, "Ana", "Manual", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-400));

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        db.Add(member);
        await db.SaveChangesAsync();

        return new World(gym.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), member.Id);
    }

    private sealed record World(Guid GymId, Guid OwnerId, Guid StaffId, Guid Member);
}
