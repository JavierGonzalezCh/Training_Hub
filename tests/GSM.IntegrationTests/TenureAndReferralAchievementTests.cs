using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Achievements.Public;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Domain.Achievements;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// L2: logros de antigüedad (<c>tenure_months</c>: días cubiertos por membresías entre 30, sin canceladas ni
/// congelados ni lo pagado por adelantado) y de referidos (<c>referrals</c>: amigos con primera membresía pagada).
/// </summary>
public sealed class TenureAndReferralAchievementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    // ---- dominio puro: días cubiertos ----

    [Fact]
    public void Covered_days_count_from_the_start_up_to_today_and_ignore_what_is_prepaid()
    {
        var gym = Guid.CreateVersion7();
        var finished = Membership.Create(gym, Guid.CreateVersion7(), Guid.CreateVersion7(), Today.AddDays(-100), 30);
        Assert.Equal(30, finished.CoveredDaysThrough(Today, []));

        var running = Membership.Create(gym, Guid.CreateVersion7(), Guid.CreateVersion7(), Today.AddDays(-9), 365);
        Assert.Equal(10, running.CoveredDaysThrough(Today, []));

        var future = Membership.Create(gym, Guid.CreateVersion7(), Guid.CreateVersion7(), Today.AddDays(3), 30);
        Assert.Equal(0, future.CoveredDaysThrough(Today, []));
    }

    [Fact]
    public void Frozen_days_and_cancelled_memberships_do_not_count()
    {
        var gym = Guid.CreateVersion7();
        var paused = Membership.Create(gym, Guid.CreateVersion7(), Guid.CreateVersion7(), Today.AddDays(-50), 90);
        // Un congelamiento cerrado de 10 días (del día -20 al -11).
        MembershipFreeze[] closed = [MembershipFreeze.Create(gym, paused.Id, Today.AddDays(-20), Today.AddDays(-11), "Viaje", false, 10, null)];
        Assert.Equal(51 - 10, paused.CoveredDaysThrough(Today, closed));

        // Congelada hoy: deja de sumar desde el día que se congeló.
        var frozenNow = Membership.Create(gym, Guid.CreateVersion7(), Guid.CreateVersion7(), Today.AddDays(-30), 90);
        MembershipFreeze[] open = [MembershipFreeze.Create(gym, frozenNow.Id, Today.AddDays(-5), null, "Lesión", false, 0, null)];
        Assert.Equal(25, frozenNow.CoveredDaysThrough(Today, open));

        var cancelled = Membership.Create(gym, Guid.CreateVersion7(), Guid.CreateVersion7(), Today.AddDays(-30), 90);
        cancelled.Cancel("Se asignó por error", DateTimeOffset.UtcNow);
        Assert.Equal(0, cancelled.CoveredDaysThrough(Today, []));
    }

    // ---- antigüedad ----

    [Fact]
    public async Task A_member_with_enough_covered_days_unlocks_tenure_once_and_is_idempotent()
    {
        var w = await SeedAsync(1);
        var member = w.Members[0];
        // 66 días cubiertos: 2 meses de 30 días.
        await AddMembershipAsync(w, member, Today.AddDays(-65), 90, price: 80000m);
        var oneMonth = await CreateAsync(w, "1 mes", AchievementConditionType.TenureMonths, 1);
        var twoMonths = await CreateAsync(w, "2 meses", AchievementConditionType.TenureMonths, 2);
        var threeMonths = await CreateAsync(w, "3 meses", AchievementConditionType.TenureMonths, 3);

        using (var scope = Staff(w))
        {
            var unlocked = await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(member);
            Assert.Contains(unlocked, a => a.AchievementId == oneMonth);
            Assert.Contains(unlocked, a => a.AchievementId == twoMonths);
            Assert.DoesNotContain(unlocked, a => a.AchievementId == threeMonths);
        }

        using (var scope = Staff(w))
        {
            var again = await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(member);
            Assert.DoesNotContain(again, a => a.AchievementId == oneMonth || a.AchievementId == twoMonths);
            Assert.Equal(1, await CountUnlocksAsync(w, oneMonth));
            Assert.Equal(2, await CountNoticesAsync(w, member));
        }
    }

    [Fact]
    public async Task Time_away_prepaid_future_and_cancelled_memberships_do_not_count_as_tenure()
    {
        var w = await SeedAsync(1);
        var member = w.Members[0];
        // Un mes de hace dos años + un año pagado por adelantado que apenas empezó hace 5 días + una cancelada.
        await AddMembershipAsync(w, member, Today.AddDays(-730), 30, price: 50000m);
        await AddMembershipAsync(w, member, Today.AddDays(-4), 365, price: 500000m);
        await AddMembershipAsync(w, member, Today.AddDays(-300), 120, price: 50000m, cancel: true);
        var achievementId = await CreateAsync(w, "2 meses", AchievementConditionType.TenureMonths, 2);

        using var scope = Staff(w);
        var unlocked = await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(member);

        // 30 + 5 = 35 días cubiertos: 1 mes, no 2.
        Assert.DoesNotContain(unlocked, a => a.AchievementId == achievementId);
        var items = await scope.ServiceProvider.GetRequiredService<GetMemberAchievementsHandler>().HandleAsync(member, default);
        Assert.Equal(1, items.Single(i => i.AchievementId == achievementId).Progress);
    }

    [Fact]
    public async Task Assigning_a_membership_evaluates_tenure_for_the_member()
    {
        var w = await SeedAsync(1);
        var member = w.Members[0];
        // Membresía vieja ya terminada (60 días) y la nueva, que se asigna ahora y dispara la evaluación.
        await AddMembershipAsync(w, member, Today.AddDays(-80), 60, price: 90000m);
        var achievementId = await CreateAsync(w, "2 meses", AchievementConditionType.TenureMonths, 2);
        var planId = await AddPlanAsync(w, 50000m);

        using (var scope = Staff(w))
        {
            await scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(member, new AssignMembershipRequest(planId, null), default);
        }

        Assert.Equal(1, await CountUnlocksAsync(w, achievementId));
    }

    // ---- referidos ----

    [Fact]
    public async Task A_referrer_unlocks_when_the_referred_friend_gets_a_paid_first_membership_and_courtesies_do_not_count()
    {
        var w = await SeedAsync(1);
        var referrer = w.Members[0];
        var courtesy = await AddMemberAsync(w, referrer);
        var paying = await AddMemberAsync(w, referrer);
        var bystander = await AddMemberAsync(w, null);
        var first = await CreateAsync(w, "Primer amigo", AchievementConditionType.Referrals, 1);
        var planId = await AddPlanAsync(w, 50000m);

        // Una cortesía de precio 0 no cuenta, aunque dispare la evaluación.
        await AddMembershipAsync(w, courtesy, Today, 30, price: 0m);
        using (var scope = Staff(w))
        {
            await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(referrer);
        }

        Assert.Equal(0, await CountUnlocksAsync(w, first));

        // El amigo que no refirió nadie no mueve nada.
        using (var scope = Staff(w))
        {
            await scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(bystander, new AssignMembershipRequest(planId, null), default);
        }

        Assert.Equal(0, await CountUnlocksAsync(w, first));

        // La primera membresía pagada del referido lo dispara por evento.
        using (var scope = Staff(w))
        {
            await scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(paying, new AssignMembershipRequest(planId, null), default);
        }

        Assert.Equal(1, await CountUnlocksAsync(w, first));
        Assert.Equal(1, await CountNoticesAsync(w, referrer));

        // Idempotente: renovar al amigo no repite el logro ni el aviso.
        using (var scope = Staff(w))
        {
            await scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(paying, new AssignMembershipRequest(planId, null), default);
        }

        Assert.Equal(1, await CountUnlocksAsync(w, first));
        Assert.Equal(1, await CountNoticesAsync(w, referrer));
    }

    [Fact]
    public async Task A_cancelled_paid_membership_does_not_count_as_a_referral()
    {
        var w = await SeedAsync(1);
        var referrer = w.Members[0];
        var friend = await AddMemberAsync(w, referrer);
        await AddMembershipAsync(w, friend, Today, 30, price: 50000m, cancel: true);
        var achievementId = await CreateAsync(w, "Primer amigo", AchievementConditionType.Referrals, 1);

        using var scope = Staff(w);
        var unlocked = await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(referrer);
        Assert.DoesNotContain(unlocked, a => a.AchievementId == achievementId);
    }

    // ---- retroactivo con los tipos nuevos ----

    [Fact]
    public async Task Retroactive_preview_and_grant_work_for_tenure_and_are_idempotent()
    {
        var w = await SeedAsync(3);
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-100), 120, price: 1m);   // 101 días: 3 meses
        await AddMembershipAsync(w, w.Members[1], Today.AddDays(-40), 60, price: 1m);     // 41 días: 1 mes
        // el miembro 2 no tiene membresías

        using (var scope = Owner(w))
        {
            var preview = await scope.ServiceProvider.GetRequiredService<PreviewRetroactiveAchievementHandler>()
                .HandleAsync(new RetroactivePreviewRequest(AchievementConditionType.TenureMonths, 2, null), default);
            Assert.Equal(1, preview.EligibleCount);
        }

        var achievementId = await CreateAsync(w, "2 meses", AchievementConditionType.TenureMonths, 2);
        Assert.Equal(1, (await GrantAsync(w, achievementId)).GrantedCount);
        Assert.Equal(0, (await GrantAsync(w, achievementId)).GrantedCount);
        Assert.Equal(1, await CountUnlocksAsync(w, achievementId));
    }

    [Fact]
    public async Task Retroactive_grant_for_referrals_counts_only_paid_friends()
    {
        var w = await SeedAsync(2);
        var strong = w.Members[0];
        var weak = w.Members[1];
        var a = await AddMemberAsync(w, strong);
        var b = await AddMemberAsync(w, strong);
        var c = await AddMemberAsync(w, weak);
        await AddMembershipAsync(w, a, Today, 30, price: 50000m);
        await AddMembershipAsync(w, b, Today, 30, price: 50000m);
        await AddMembershipAsync(w, c, Today, 30, price: 0m);   // cortesía: no cuenta

        using (var scope = Owner(w))
        {
            var preview = await scope.ServiceProvider.GetRequiredService<PreviewRetroactiveAchievementHandler>()
                .HandleAsync(new RetroactivePreviewRequest(AchievementConditionType.Referrals, 2, null), default);
            Assert.Equal(1, preview.EligibleCount);
        }

        var achievementId = await CreateAsync(w, "Dos amigos", AchievementConditionType.Referrals, 2);
        Assert.Equal(1, (await GrantAsync(w, achievementId)).GrantedCount);

        using var verify = Owner(w);
        var rows = await verify.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberAchievement>()
            .Where(u => u.AchievementId == achievementId).ToListAsync();
        Assert.Equal(strong, rows.Single().MemberId);
    }

    [Fact]
    public async Task New_gyms_get_the_one_year_and_first_friend_defaults()
    {
        var w = await SeedAsync(0);
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var seed = scope.ServiceProvider
                .GetServices<GSM.Application.Common.Events.IDomainEventHandler<GSM.Application.Gyms.Events.GymCreated>>()
                .OfType<SeedDefaultAchievementsHandler>().Single();
            await seed.HandleAsync(
                new GSM.Application.Gyms.Events.GymCreated(w.GymId, "Tenure Gym", GymPlan.Basic), default);
        }

        using var verify = Owner(w);
        var all = await verify.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Achievement>()
            .Where(a => a.GymId == w.GymId).ToListAsync();
        Assert.Contains(all, a => a.ConditionType == AchievementConditionType.TenureMonths && a.ConditionValue == 12);
        Assert.Contains(all, a => a.ConditionType == AchievementConditionType.Referrals && a.ConditionValue == 1);
        Assert.Equal(7, all.Count);
    }

    // ---- helpers ----

    private IServiceScope Owner(World w) => ScopeAs(w, UserRole.Owner);

    private IServiceScope Staff(World w) => ScopeAs(w, UserRole.Staff);

    private IServiceScope ScopeAs(World w, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.OwnerId, w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<Guid> CreateAsync(World w, string name, AchievementConditionType type, int value)
    {
        using var scope = Owner(w);
        var created = await scope.ServiceProvider.GetRequiredService<CreateAchievementHandler>().HandleAsync(
            new CreateAchievementRequest(name, null, null, type, value), default);
        return created.Id;
    }

    private async Task<RetroactiveGrantResponse> GrantAsync(World w, Guid achievementId)
    {
        using var scope = Owner(w);
        return await scope.ServiceProvider.GetRequiredService<GrantRetroactiveAchievementHandler>()
            .HandleAsync(achievementId, default);
    }

    private async Task<int> CountUnlocksAsync(World w, Guid achievementId)
    {
        using var scope = Owner(w);
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberAchievement>()
            .CountAsync(u => u.AchievementId == achievementId);
    }

    private async Task<int> CountNoticesAsync(World w, Guid memberId)
    {
        using var scope = Owner(w);
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .CountAsync(n => n.RecipientMemberId == memberId && n.Type == NotificationType.AchievementUnlocked);
    }

    private async Task<Guid> AddPlanAsync(World w, decimal price)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var plan = MembershipPlan.Create(w.GymId, $"Plan {Guid.NewGuid():N}"[..14], null, price, 30);
        db.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private async Task AddMembershipAsync(World w, Guid memberId, DateOnly start, int days, decimal price, bool cancel = false)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var membership = Membership.Create(
            w.GymId, memberId, Guid.CreateVersion7(), start, days,
            pricing: new MembershipPricing(price, null, 0m, null));
        if (cancel)
        {
            membership.Cancel("Asignada por error", DateTimeOffset.UtcNow);
        }

        db.Add(membership);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AddMemberAsync(World w, Guid? referredBy)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var member = Member.Create(
            w.GymId,
            new MemberProfile(
                "Amigo", "Referido", DocumentType.Cc, $"doc-{Guid.NewGuid():N}"[..12], null,
                ReferredByMemberId: referredBy),
            Today.AddDays(-10), Today);
        db.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    private async Task<World> SeedAsync(int memberCount)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Tenure Gym", $"tn-{Guid.NewGuid():N}"[..16]);
        var ownerId = Guid.CreateVersion7();
        var members = Enumerable.Range(0, memberCount)
            .Select(i => Member.Create(gym.Id, $"M{i}", "Tenure", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-400)))
            .ToList();

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        db.AddRange(members);
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerId, members.Select(m => m.Id).ToList());
    }

    private sealed record World(Guid GymId, Guid OwnerId, IReadOnlyList<Guid> Members);
}
