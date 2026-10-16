using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Achievements.Public;
using GSM.Application.Attendance;
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
/// L5: premios de los logros. Texto y plazo en el logro, pendiente al ganarlo, entregado por el staff (quién y
/// cuándo), vencido calculado (sin tarea), visible en la tarjeta de recepción, en la ficha y en "mis logros",
/// con aviso programado 3 días antes del vencimiento.
/// </summary>
public sealed class AchievementRewardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    [Fact]
    public async Task Winning_an_achievement_with_a_reward_leaves_it_pending_with_its_expiry()
    {
        var w = await SeedAsync();
        var achievementId = await CreateAsync(w, "Constante", "Camiseta del gym", 10);

        await UnlockAsync(w);

        var item = await ItemAsync(w, achievementId, Today);
        Assert.True(item.Unlocked);
        Assert.Equal("Camiseta del gym", item.RewardDescription);
        Assert.Equal("Pending", item.RewardStatus);
        Assert.Equal(AtLocal(Today, 12).AddDays(10), item.RewardExpiresAt);
        Assert.Null(item.RewardDeliveredAt);
    }

    [Fact]
    public async Task An_achievement_without_a_reward_stays_plain_recognition()
    {
        var w = await SeedAsync();
        var achievementId = await CreateAsync(w, "Solo honor", null, null);

        await UnlockAsync(w);

        var item = await ItemAsync(w, achievementId, Today);
        Assert.True(item.Unlocked);
        Assert.Null(item.RewardDescription);
        Assert.Equal("None", item.RewardStatus);
        Assert.Empty(await PendingCardRewardsAsync(w, Today));
        await using var scope = new AsyncScope(Staff(w, Today));
        await Assert.ThrowsAsync<ConflictException>(() => scope.Services
            .GetRequiredService<DeliverRewardHandler>().HandleAsync(achievementId, w.MemberId, default));
    }

    [Fact]
    public async Task A_reward_without_a_deadline_never_expires()
    {
        var w = await SeedAsync();
        var achievementId = await CreateAsync(w, "Para siempre", "Botella", null);
        await UnlockAsync(w);

        var later = await ItemAsync(w, achievementId, Today.AddDays(400));
        Assert.Equal("Pending", later.RewardStatus);
        Assert.Null(later.RewardExpiresAt);
    }

    [Fact]
    public async Task Staff_delivers_the_reward_and_it_records_who_and_when_and_cannot_be_delivered_twice()
    {
        var w = await SeedAsync();
        var achievementId = await CreateAsync(w, "Constante", "Camiseta del gym", 10);
        await UnlockAsync(w);

        RewardDeliveredResponse delivered;
        using (var scope = Staff(w, Today))
        {
            delivered = await scope.ServiceProvider.GetRequiredService<DeliverRewardHandler>()
                .HandleAsync(achievementId, w.MemberId, default);
        }

        Assert.Equal("Camiseta del gym", delivered.RewardDescription);
        Assert.Equal(AtLocal(Today, 12), delivered.DeliveredAt);

        using (var scope = Owner(w, Today))
        {
            var row = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberAchievement>()
                .SingleAsync(u => u.AchievementId == achievementId);
            Assert.Equal(RewardStatus.Delivered, row.RewardStatus);
            Assert.Equal(w.StaffId, row.RewardDeliveredBy);
            Assert.Equal(AtLocal(Today, 12), row.RewardDeliveredAt);
        }

        var item = await ItemAsync(w, achievementId, Today);
        Assert.Equal("Delivered", item.RewardStatus);
        Assert.Equal(AtLocal(Today, 12), item.RewardDeliveredAt);
        Assert.Empty(await PendingCardRewardsAsync(w, Today));

        using (var scope = Staff(w, Today))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<DeliverRewardHandler>().HandleAsync(achievementId, w.MemberId, default));
        }
    }

    [Fact]
    public async Task An_unclaimed_reward_expires_by_calculation_and_can_no_longer_be_delivered()
    {
        var w = await SeedAsync();
        var achievementId = await CreateAsync(w, "Constante", "Camiseta del gym", 10);
        await UnlockAsync(w);

        // Un día antes del plazo sigue vigente.
        Assert.Equal("Pending", (await ItemAsync(w, achievementId, Today.AddDays(9))).RewardStatus);
        Assert.Single(await PendingCardRewardsAsync(w, Today.AddDays(9)));

        // Pasado el plazo: vencido, sin tarea programada que lo haya tocado.
        var late = Today.AddDays(11);
        Assert.Equal("Expired", (await ItemAsync(w, achievementId, late)).RewardStatus);
        Assert.Empty(await PendingCardRewardsAsync(w, late));
        using (var scope = Staff(w, late))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<DeliverRewardHandler>().HandleAsync(achievementId, w.MemberId, default));
        }

        using (var scope = Owner(w, late))
        {
            var overview = await scope.ServiceProvider.GetRequiredService<ListRewardsHandler>().HandleAsync(null, default);
            Assert.Equal(1, overview.Expired);
            Assert.Equal(0, overview.Pending);
        }
    }

    [Fact]
    public async Task The_reception_card_lists_the_pending_rewards_with_what_to_hand_over()
    {
        var w = await SeedAsync();
        var achievementId = await CreateAsync(w, "Constante", "Camiseta del gym", 10);
        await UnlockAsync(w);

        var rewards = await PendingCardRewardsAsync(w, Today);

        var reward = Assert.Single(rewards);
        Assert.Equal(achievementId, reward.AchievementId);
        Assert.Equal("Constante", reward.AchievementName);
        Assert.Equal("Camiseta del gym", reward.RewardDescription);
        Assert.Equal(AtLocal(Today, 12).AddDays(10), reward.ExpiresAt);
    }

    [Fact]
    public async Task Editing_the_achievement_does_not_change_the_reward_already_promised()
    {
        var w = await SeedAsync();
        var achievementId = await CreateAsync(w, "Constante", "Camiseta del gym", 10);
        await UnlockAsync(w);

        using (var scope = Owner(w, Today))
        {
            var updated = await scope.ServiceProvider.GetRequiredService<UpdateAchievementHandler>().HandleAsync(
                achievementId,
                new UpdateAchievementRequest("Constante", null, null, AchievementConditionType.Visits, 1, "Gorra", 3), default);
            Assert.Equal("Gorra", updated.RewardDescription);
            Assert.Equal(3, updated.RewardValidDays);
        }

        var item = await ItemAsync(w, achievementId, Today);
        Assert.Equal("Camiseta del gym", item.RewardDescription);
        Assert.Equal(AtLocal(Today, 12).AddDays(10), item.RewardExpiresAt);
    }

    [Fact]
    public async Task A_retroactive_grant_leaves_the_reward_pending_for_everyone_who_received_it()
    {
        var w = await SeedAsync();
        await AddVisitAsync(w);

        // El logro se crea con el miembro ya cumpliendo la condición; el Owner lo otorga retroactivamente.
        var achievementId = await CreateAsync(w, "Retro", "Pase gratis", 5);
        using (var scope = Owner(w, Today))
        {
            var granted = await scope.ServiceProvider.GetRequiredService<GrantRetroactiveAchievementHandler>()
                .HandleAsync(achievementId, default);
            Assert.Equal(1, granted.GrantedCount);
        }

        var item = await ItemAsync(w, achievementId, Today);
        Assert.Equal("Pending", item.RewardStatus);
        Assert.Equal("Pase gratis", item.RewardDescription);
    }

    [Fact]
    public async Task A_manual_achievement_with_a_reward_is_pending_and_a_revoked_one_has_nothing_to_claim()
    {
        var w = await SeedAsync();
        var achievementId = await CreateManualAsync(w, "Campeón", "Trofeo", 30);

        using (var scope = Staff(w, Today))
        {
            var granted = await scope.ServiceProvider.GetRequiredService<GrantManualAchievementHandler>().HandleAsync(
                achievementId, new GrantManualAchievementRequest(w.MemberId, "Ganó la competencia"), default);
            Assert.Equal("Pending", granted.RewardStatus);
            Assert.Equal("Trofeo", granted.RewardDescription);
        }

        Assert.Single(await PendingCardRewardsAsync(w, Today));

        using (var scope = Staff(w, Today))
        {
            await scope.ServiceProvider.GetRequiredService<RevokeManualAchievementHandler>().HandleAsync(
                achievementId, w.MemberId, new RevokeManualAchievementRequest("Se otorgó por error"), default);
        }

        // Retirado: ya no aparece para reclamar, no se puede entregar y no cuenta en el resumen del Owner.
        Assert.Empty(await PendingCardRewardsAsync(w, Today));
        using (var scope = Staff(w, Today))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<DeliverRewardHandler>().HandleAsync(achievementId, w.MemberId, default));
        }

        using (var scope = Owner(w, Today))
        {
            var overview = await scope.ServiceProvider.GetRequiredService<ListRewardsHandler>().HandleAsync(null, default);
            Assert.Equal(0, overview.Pending);
            Assert.Empty(overview.Items);
        }
    }

    [Fact]
    public async Task The_owner_sees_pending_delivered_and_expired_rewards_and_can_filter_them()
    {
        var w = await SeedAsync();
        var first = await CreateAsync(w, "Uno", "Camiseta", 10);
        var second = await CreateAsync(w, "Dos", "Gorra", 2);
        var third = await CreateAsync(w, "Tres", "Botella", null);
        _ = (first, second);
        await UnlockAsync(w);

        using (var scope = Staff(w, Today))
        {
            await scope.ServiceProvider.GetRequiredService<DeliverRewardHandler>().HandleAsync(third, w.MemberId, default);
        }

        // Tres días después: "Gorra" (2 días) venció, "Camiseta" sigue pendiente, "Botella" se entregó.
        using var owner = Owner(w, Today.AddDays(3));
        var list = owner.ServiceProvider.GetRequiredService<ListRewardsHandler>();

        var all = await list.HandleAsync(null, default);
        Assert.Equal(1, all.Pending);
        Assert.Equal(1, all.Delivered);
        Assert.Equal(1, all.Expired);
        Assert.Equal(3, all.Items.Count);
        Assert.All(all.Items, i => Assert.Equal("Ana Premio", i.MemberName));
        Assert.Equal(w.StaffId, all.Items.Single(i => i.Status == "Delivered").DeliveredBy);

        var pending = await list.HandleAsync("pending", default);
        Assert.Equal("Camiseta", Assert.Single(pending.Items).RewardDescription);
        Assert.Equal("Gorra", Assert.Single((await list.HandleAsync("expired", default)).Items).RewardDescription);
        Assert.Equal("Botella", Assert.Single((await list.HandleAsync("delivered", default)).Items).RewardDescription);

        await Assert.ThrowsAsync<ValidationDomainException>(() => list.HandleAsync("none", default));
    }

    [Fact]
    public async Task A_reward_needs_a_real_text_and_a_sane_deadline_and_a_deadline_without_text_is_dropped()
    {
        var w = await SeedAsync();
        using var scope = Owner(w, Today);
        var create = scope.ServiceProvider.GetRequiredService<CreateAchievementHandler>();

        await Assert.ThrowsAsync<ValidationDomainException>(() => create.HandleAsync(
            new CreateAchievementRequest("Corto", null, null, AchievementConditionType.Visits, 1, "ab", null), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() => create.HandleAsync(
            new CreateAchievementRequest("Plazo", null, null, AchievementConditionType.Visits, 1, "Camiseta", 0), default));

        var noText = await create.HandleAsync(
            new CreateAchievementRequest("Sin texto", null, null, AchievementConditionType.Visits, 1, "  ", 10), default);
        Assert.Null(noText.RewardDescription);
        Assert.Null(noText.RewardValidDays);
    }

    [Fact]
    public async Task Another_gym_cannot_deliver_a_reward_it_does_not_own()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        var achievementId = await CreateAsync(w, "Constante", "Camiseta del gym", 10);
        await UnlockAsync(w);

        using var scope = Staff(other, Today);
        await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
            .GetRequiredService<DeliverRewardHandler>().HandleAsync(achievementId, w.MemberId, default));
    }

    // ---- aviso de vencimiento ----

    [Fact]
    public async Task A_reminder_is_scheduled_three_days_before_the_expiry_at_eight()
    {
        var w = await SeedAsync();
        await CreateAsync(w, "Constante", "Camiseta del gym", 10);
        await UnlockAsync(w);

        // Vence el 15 de octubre: el aviso sale el 12 a las 8:00.
        var notice = Assert.Single(await RewardNoticesAsync(w));
        Assert.Equal(AtLocal(new DateOnly(2026, 10, 12), 8), notice.ScheduledFor);
        Assert.Contains("Camiseta del gym", notice.Title);
        Assert.Contains("15 oct", notice.Title);
        Assert.Equal(NotificationStatus.Pending, notice.Status);
    }

    [Fact]
    public async Task A_reward_that_expires_in_less_than_three_days_gets_its_reminder_right_away()
    {
        var w = await SeedAsync();
        await CreateAsync(w, "Rápido", "Camiseta", 2);
        await UnlockAsync(w);

        var notice = Assert.Single(await RewardNoticesAsync(w));
        Assert.Equal(AtLocal(Today, 12), notice.ScheduledFor);
    }

    [Fact]
    public async Task A_reward_without_a_deadline_sends_no_reminder()
    {
        var w = await SeedAsync();
        await CreateAsync(w, "Sin plazo", "Camiseta", null);
        await UnlockAsync(w);

        Assert.Empty(await RewardNoticesAsync(w));
    }

    [Fact]
    public async Task Delivering_the_reward_cancels_the_reminder_that_had_not_gone_out()
    {
        var w = await SeedAsync();
        var achievementId = await CreateAsync(w, "Constante", "Camiseta del gym", 10);
        await UnlockAsync(w);

        using (var scope = Staff(w, Today))
        {
            await scope.ServiceProvider.GetRequiredService<DeliverRewardHandler>()
                .HandleAsync(achievementId, w.MemberId, default);
        }

        Assert.Equal(NotificationStatus.Cancelled, Assert.Single(await RewardNoticesAsync(w)).Status);
    }

    [Fact]
    public async Task Revoking_a_manual_achievement_cancels_its_reward_reminder()
    {
        var w = await SeedAsync();
        var achievementId = await CreateManualAsync(w, "Campeón", "Trofeo", 30);
        using (var scope = Staff(w, Today))
        {
            await scope.ServiceProvider.GetRequiredService<GrantManualAchievementHandler>().HandleAsync(
                achievementId, new GrantManualAchievementRequest(w.MemberId, "Ganó la competencia"), default);
        }

        Assert.Equal(NotificationStatus.Pending, Assert.Single(await RewardNoticesAsync(w)).Status);

        using (var scope = Staff(w, Today))
        {
            await scope.ServiceProvider.GetRequiredService<RevokeManualAchievementHandler>().HandleAsync(
                achievementId, w.MemberId, new RevokeManualAchievementRequest("Se otorgó por error"), default);
        }

        Assert.Equal(NotificationStatus.Cancelled, Assert.Single(await RewardNoticesAsync(w)).Status);
    }

    // ---- helpers ----

    private sealed class AsyncScope(IServiceScope scope) : IAsyncDisposable
    {
        public IServiceProvider Services => scope.ServiceProvider;

        public ValueTask DisposeAsync()
        {
            scope.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private IServiceScope Owner(World w, DateOnly day) => ScopeAs(w, w.OwnerId, UserRole.Owner, day);

    private IServiceScope Staff(World w, DateOnly day) => ScopeAs(w, w.StaffId, UserRole.Staff, day);

    private IServiceScope ScopeAs(World w, Guid userId, UserRole role, DateOnly day)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
        return scope;
    }

    private static DateTimeOffset AtLocal(DateOnly day, int hour)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(hour);

    private async Task<Guid> CreateAsync(World w, string name, string? reward, int? validDays)
    {
        using var scope = Owner(w, Today);
        var created = await scope.ServiceProvider.GetRequiredService<CreateAchievementHandler>().HandleAsync(
            new CreateAchievementRequest(name, null, null, AchievementConditionType.Visits, 1, reward, validDays), default);
        return created.Id;
    }

    private async Task<Guid> CreateManualAsync(World w, string name, string? reward, int? validDays)
    {
        using var scope = Owner(w, Today);
        var created = await scope.ServiceProvider.GetRequiredService<CreateAchievementHandler>().HandleAsync(
            new CreateAchievementRequest(name, null, null, AchievementConditionType.Manual, 1, reward, validDays), default);
        return created.Id;
    }

    /// <summary>Una visita hoy y la evaluación de logros (lo que haría el check-in): gana todos los de "1 visita".</summary>
    private async Task UnlockAsync(World w)
    {
        await AddVisitAsync(w);
        using var scope = Owner(w, Today);
        await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(w.MemberId);
    }

    private async Task AddVisitAsync(World w)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        if (await db.Set<AttendanceEntity>().AnyAsync(a => a.MemberId == w.MemberId))
        {
            return;
        }

        db.Add(AttendanceEntity.Create(
            w.GymId, w.MemberId, GSM.Domain.Attendance.AttendanceMethod.Manual, AtLocal(Today, 12), Today));
        await db.SaveChangesAsync();
    }

    private async Task<MemberAchievementItem> ItemAsync(World w, Guid achievementId, DateOnly day)
    {
        using var scope = Owner(w, day);
        var items = await scope.ServiceProvider.GetRequiredService<GetMemberAchievementsHandler>()
            .HandleAsync(w.MemberId, default);
        return items.Single(i => i.AchievementId == achievementId);
    }

    private async Task<IReadOnlyList<GSM.Application.Attendance.Contracts.ReceptionRewardResponse>> PendingCardRewardsAsync(
        World w, DateOnly day)
    {
        using var scope = Staff(w, day);
        var card = await scope.ServiceProvider.GetRequiredService<GetReceptionCardHandler>().HandleAsync(w.MemberId, default);
        return card.PendingRewards;
    }

    private async Task<List<Notification>> RewardNoticesAsync(World w)
    {
        using var scope = Owner(w, Today);
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .AsNoTracking()
            .Where(n => n.GymId == w.GymId && n.Type == NotificationType.RewardExpiring)
            .OrderBy(n => n.ScheduledFor)
            .ToListAsync();
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Reward Gym", $"rw-{Guid.NewGuid():N}"[..16]);
        var member = Member.Create(gym.Id, "Ana", "Premio", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-400));

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        db.Add(member);
        await db.SaveChangesAsync();

        return new World(gym.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), member.Id);
    }

    private sealed record World(Guid GymId, Guid OwnerId, Guid StaffId, Guid MemberId);
}
