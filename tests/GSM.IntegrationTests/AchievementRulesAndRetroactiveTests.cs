using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Achievements.Public;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Public;
using GSM.Domain.Achievements;
using GSM.Domain.Attendance;
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
/// L1: qué entradas cuentan para los logros (días, no entradas; las anuladas no cuentan y anular no quita
/// logros ya ganados) y los logros retroactivos (aviso previo, otorgamiento, idempotencia, un aviso por miembro).
/// </summary>
public sealed class AchievementRulesAndRetroactiveTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    [Fact]
    public async Task Several_entries_on_the_same_day_count_as_one_day_for_achievements()
    {
        var w = await SeedAsync(1);
        var member = w.Members[0];
        await AddEntriesAsync(w, member, Today, 3);

        using var scope = Staff(w);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IAttendanceReader>().GetVisitCountAsync(member));
        Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<IAttendanceReader>()
            .GetVisitCountsAsync([member])).GetValueOrDefault(member));

        // Una condición de 2 visitas no se cumple con tres entradas del mismo día.
        var achievementId = await CreateAsync(w, "Dos visitas", 2);
        var evaluated = await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(member);
        Assert.DoesNotContain(evaluated, a => a.AchievementId == achievementId);
    }

    [Fact]
    public async Task A_voided_entry_does_not_count_and_voiding_keeps_an_achievement_already_earned()
    {
        var w = await SeedAsync(1);
        var member = w.Members[0];
        await AddEntriesAsync(w, member, Today, 1);
        var first = await CreateAsync(w, "Primera visita", 1);

        using (var scope = Staff(w))
        {
            var unlocked = await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(member);
            Assert.Contains(unlocked, a => a.AchievementId == first);
        }

        Guid attendanceId;
        using (var scope = Staff(w))
        {
            attendanceId = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<AttendanceEntity>()
                .Where(a => a.MemberId == member).Select(a => a.Id).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<VoidCheckInHandler>()
                .HandleAsync(attendanceId, new VoidCheckInRequest("Registré al miembro equivocado"), default);
        }

        using (var scope = Staff(w))
        {
            // La entrada anulada ya no suma visitas...
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IAttendanceReader>().GetVisitCountAsync(member));

            // ...pero el logro ya ganado se conserva.
            var items = await scope.ServiceProvider.GetRequiredService<GetMemberAchievementsHandler>().HandleAsync(member, default);
            Assert.True(items.Single(i => i.AchievementId == first).Unlocked);
        }
    }

    [Fact]
    public async Task A_voided_entry_does_not_unlock_an_achievement_later()
    {
        var w = await SeedAsync(1);
        var member = w.Members[0];
        await AddEntriesAsync(w, member, Today, 1, voided: true);
        var achievementId = await CreateAsync(w, "Primera visita", 1);

        using var scope = Staff(w);
        var unlocked = await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(member);
        Assert.DoesNotContain(unlocked, a => a.AchievementId == achievementId);
    }

    [Fact]
    public async Task Preview_counts_only_active_members_who_already_qualify_by_days_and_ignores_voided_entries()
    {
        var w = await SeedAsync(5);
        await AddDaysAsync(w, w.Members[0], 3);                        // cumple
        await AddEntriesAsync(w, w.Members[1], Today, 5);              // 5 entradas, 1 día: no cumple
        await AddDaysAsync(w, w.Members[2], 3, voided: true);          // anuladas: no cumple
        await AddDaysAsync(w, w.Members[3], 4);                        // cumple
        await AddDaysAsync(w, w.Members[4], 6);                        // inactivo: no cuenta
        await DeactivateAsync(w, w.Members[4]);

        using var scope = Owner(w);
        var preview = await scope.ServiceProvider.GetRequiredService<PreviewRetroactiveAchievementHandler>()
            .HandleAsync(new RetroactivePreviewRequest(AchievementConditionType.Visits, 3, null), default);

        Assert.Equal(2, preview.EligibleCount);
    }

    [Fact]
    public async Task Granting_unlocks_for_those_who_qualify_dated_today_with_a_single_notice_each_and_is_idempotent()
    {
        var w = await SeedAsync(3);
        await AddDaysAsync(w, w.Members[0], 3);
        await AddDaysAsync(w, w.Members[1], 1);
        await AddDaysAsync(w, w.Members[2], 5);
        var achievementId = await CreateAsync(w, "Constante", 3);

        RetroactiveGrantResponse first;
        using (var scope = Owner(w))
        {
            first = await scope.ServiceProvider.GetRequiredService<GrantRetroactiveAchievementHandler>()
                .HandleAsync(achievementId, default);
        }

        Assert.Equal(2, first.GrantedCount);

        using (var scope = Owner(w))
        {
            var rows = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberAchievement>()
                .Where(u => u.AchievementId == achievementId).ToListAsync();
            Assert.Equal(
                new[] { w.Members[0], w.Members[2] }.Order(),
                rows.Select(r => r.MemberId).Order());
            Assert.All(rows, r => Assert.Equal(Today, DateOnly.FromDateTime(r.UnlockedAt.UtcDateTime.AddHours(-5))));
        }

        // Un solo aviso por miembro que lo recibió; el que no lo cumple no recibe nada.
        Assert.Equal(1, await CountNoticesAsync(w, w.Members[0]));
        Assert.Equal(1, await CountNoticesAsync(w, w.Members[2]));
        Assert.Equal(0, await CountNoticesAsync(w, w.Members[1]));

        // Segunda ejecución: nadie nuevo, ni filas ni avisos repetidos.
        using (var scope = Owner(w))
        {
            var second = await scope.ServiceProvider.GetRequiredService<GrantRetroactiveAchievementHandler>()
                .HandleAsync(achievementId, default);
            Assert.Equal(0, second.GrantedCount);
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberAchievement>()
                .CountAsync(u => u.AchievementId == achievementId));
        }

        Assert.Equal(1, await CountNoticesAsync(w, w.Members[0]));
    }

    [Fact]
    public async Task Lowering_the_condition_grants_to_new_qualifiers_and_raising_it_takes_nothing_away()
    {
        var w = await SeedAsync(2);
        await AddDaysAsync(w, w.Members[0], 5);
        await AddDaysAsync(w, w.Members[1], 3);
        var achievementId = await CreateAsync(w, "Habitual", 5);

        await GrantAsync(w, achievementId);                             // solo el miembro 0
        Assert.Equal(1, await CountUnlocksAsync(w, achievementId));

        // Subir el valor no quita el logro a quien ya lo tenía.
        await UpdateAsync(w, achievementId, 10);
        using (var scope = Owner(w))
        {
            Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<PreviewRetroactiveAchievementHandler>()
                .HandleAsync(new RetroactivePreviewRequest(AchievementConditionType.Visits, 10, achievementId), default)).EligibleCount);
        }

        Assert.Equal(1, await CountUnlocksAsync(w, achievementId));

        // Bajarlo a 3: el aviso previo cuenta solo al miembro 1 (el 0 ya lo tiene) y el otorgamiento lo alcanza.
        using (var scope = Owner(w))
        {
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<PreviewRetroactiveAchievementHandler>()
                .HandleAsync(new RetroactivePreviewRequest(AchievementConditionType.Visits, 3, achievementId), default)).EligibleCount);
        }

        await UpdateAsync(w, achievementId, 3);
        Assert.Equal(1, (await GrantAsync(w, achievementId)).GrantedCount);
        Assert.Equal(2, await CountUnlocksAsync(w, achievementId));
    }

    [Fact]
    public async Task Granting_an_inactive_or_missing_achievement_is_rejected()
    {
        var w = await SeedAsync(1);
        var achievementId = await CreateAsync(w, "Viejo", 1);
        using (var scope = Owner(w))
        {
            await scope.ServiceProvider.GetRequiredService<DeleteAchievementHandler>().HandleAsync(achievementId, default);
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<GrantRetroactiveAchievementHandler>().HandleAsync(achievementId, default));
            await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
                .GetRequiredService<GrantRetroactiveAchievementHandler>().HandleAsync(Guid.CreateVersion7(), default));
        }
    }

    [Fact]
    public async Task An_achievement_of_another_gym_is_invisible_to_the_grant()
    {
        var w = await SeedAsync(1);
        var other = await SeedAsync(1);
        var achievementId = await CreateAsync(w, "Mío", 1);

        using var scope = Owner(other);
        await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
            .GetRequiredService<GrantRetroactiveAchievementHandler>().HandleAsync(achievementId, default));
    }

    // ---- helpers ----

    private IServiceScope Owner(World w) => ScopeAs(w, w.OwnerId, UserRole.Owner);

    private IServiceScope Staff(World w) => ScopeAs(w, w.OwnerId, UserRole.Staff);

    private IServiceScope ScopeAs(World w, Guid userId, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<Guid> CreateAsync(World w, string name, int visits)
    {
        using var scope = Owner(w);
        var created = await scope.ServiceProvider.GetRequiredService<CreateAchievementHandler>().HandleAsync(
            new CreateAchievementRequest(name, null, null, AchievementConditionType.Visits, visits), default);
        return created.Id;
    }

    private async Task UpdateAsync(World w, Guid achievementId, int visits)
    {
        using var scope = Owner(w);
        await scope.ServiceProvider.GetRequiredService<UpdateAchievementHandler>().HandleAsync(
            achievementId,
            new UpdateAchievementRequest("Habitual", null, null, AchievementConditionType.Visits, visits), default);
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

    private async Task DeactivateAsync(World w, Guid memberId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        (await db.Set<Member>().SingleAsync(m => m.Id == memberId)).Deactivate();
        await db.SaveChangesAsync();
    }

    /// <summary>Una entrada por día, en los <paramref name="days"/> días que terminan hoy.</summary>
    private Task AddDaysAsync(World w, Guid memberId, int days, bool voided = false)
        => AddRowsAsync(w, memberId, Enumerable.Range(0, days).Select(i => Today.AddDays(-i)).ToList(), 1, voided);

    private Task AddEntriesAsync(World w, Guid memberId, DateOnly day, int entries, bool voided = false)
        => AddRowsAsync(w, memberId, [day], entries, voided);

    private async Task AddRowsAsync(World w, Guid memberId, IReadOnlyList<DateOnly> days, int perDay, bool voided)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var now = DateTimeOffset.UtcNow;

        foreach (var day in days)
        {
            for (var i = 0; i < perDay; i++)
            {
                var entry = AttendanceEntity.Create(
                    w.GymId, memberId, AttendanceMethod.Manual, now.AddHours(i), day);
                if (voided)
                {
                    entry.Void(w.OwnerId, "Entrada de prueba anulada", now);
                }

                db.Add(entry);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task<World> SeedAsync(int memberCount)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Retro Gym", $"rt-{Guid.NewGuid():N}"[..16]);
        var ownerId = Guid.CreateVersion7();
        var members = Enumerable.Range(0, memberCount)
            .Select(i => Member.Create(gym.Id, $"M{i}", "Retro", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-400)))
            .ToList();

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        db.AddRange(members);
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerId, members.Select(m => m.Id).ToList());
    }

    private sealed record World(Guid GymId, Guid OwnerId, IReadOnlyList<Guid> Members);
}
