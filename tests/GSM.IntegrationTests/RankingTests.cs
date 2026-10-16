using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using GSM.Domain.Achievements;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.IntegrationTests;

/// <summary>
/// L6: ranking mensual voluntario de constancia. Solo aparece quien acepta; nombre con inicial o alias; sin menores;
/// días (no entradas) sin anuladas; empate gana quien llegó antes; top 10 + posición propia + podio del mes anterior;
/// el gym puede apagarlo.
/// </summary>
public sealed class RankingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    // ---------- participar ----------

    [Fact]
    public async Task Only_members_who_joined_appear_and_the_real_name_shows_the_surname_initial_with_photo()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres", photoUrl: "https://cdn.test/ana.jpg");
        var beto = await AddMemberAsync(w, "Beto", "Rojas");
        await AddVisitsAsync(w, ana, Day(1), Day(2));
        await AddVisitsAsync(w, beto, Day(1), Day(2), Day(3));
        await JoinAsync(w, ana, useAlias: false, alias: null);

        var ranking = await GetAsync(w, beto);

        var entry = Assert.Single(ranking.Top);
        Assert.Equal("Ana T.", entry.DisplayName);
        Assert.Equal("https://cdn.test/ana.jpg", entry.PhotoUrl);
        Assert.Equal(2, entry.Days);
        Assert.False(ranking.Me.Participating);
        Assert.True(ranking.Me.CanParticipate);
        Assert.Null(ranking.Me.Position);
    }

    [Fact]
    public async Task An_alias_replaces_the_name_and_hides_the_photo()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres", photoUrl: "https://cdn.test/ana.jpg");
        await AddVisitsAsync(w, ana, Day(1));
        await JoinAsync(w, ana, useAlias: true, alias: "  Fuerza21 ");

        var ranking = await GetAsync(w, ana);

        var entry = Assert.Single(ranking.Top);
        Assert.Equal("Fuerza21", entry.DisplayName);
        Assert.Null(entry.PhotoUrl);
        Assert.True(entry.IsMe);
        Assert.True(ranking.Me.UsesAlias);
        Assert.Equal("Fuerza21", ranking.Me.Alias);
    }

    [Fact]
    public async Task Joining_again_changes_how_the_member_is_shown_instead_of_duplicating()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres");
        await AddVisitsAsync(w, ana, Day(1));
        await JoinAsync(w, ana, useAlias: false, alias: null);
        await JoinAsync(w, ana, useAlias: true, alias: "Fuerza21");

        var ranking = await GetAsync(w, ana);

        Assert.Equal("Fuerza21", Assert.Single(ranking.Top).DisplayName);
        Assert.Equal(1, await CountParticipantsAsync(w));
    }

    [Fact]
    public async Task Leaving_removes_the_member_from_the_ranking_and_leaving_twice_is_harmless()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres");
        await AddVisitsAsync(w, ana, Day(1));
        await JoinAsync(w, ana, useAlias: false, alias: null);

        await LeaveAsync(w, ana);
        await LeaveAsync(w, ana);

        var ranking = await GetAsync(w, ana);
        Assert.Empty(ranking.Top);
        Assert.False(ranking.Me.Participating);
        Assert.Equal(0, await CountParticipantsAsync(w));
    }

    [Fact]
    public async Task A_minor_cannot_join_and_is_told_why_in_the_ranking()
    {
        var w = await SeedAsync();
        var kid = await AddMemberAsync(w, "Nico", "Pérez", birthDate: Today.AddYears(-16));

        var ex = await Assert.ThrowsAsync<ValidationDomainException>(() => JoinAsync(w, kid, false, null));
        Assert.Contains("menores", ex.Message);

        var ranking = await GetAsync(w, kid);
        Assert.False(ranking.Me.CanParticipate);
        Assert.Contains("menores", ranking.Me.CannotParticipateReason);
    }

    [Fact]
    public async Task A_member_without_birth_date_cannot_join_because_the_age_is_unknown()
    {
        var w = await SeedAsync();
        var legacy = await AddMemberAsync(w, "Lola", "Gómez", unknownBirthDate: true);

        await Assert.ThrowsAsync<ValidationDomainException>(() => JoinAsync(w, legacy, false, null));
    }

    [Fact]
    public async Task A_participant_who_later_turns_out_to_be_a_minor_or_inactive_is_left_out()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres");
        var beto = await AddMemberAsync(w, "Beto", "Rojas");
        await AddVisitsAsync(w, ana, Day(1), Day(2));
        await AddVisitsAsync(w, beto, Day(1));
        await JoinAsync(w, ana, false, null);
        await JoinAsync(w, beto, false, null);

        await DeactivateAsync(ana);

        var ranking = await GetAsync(w, beto);
        Assert.Equal("Beto R.", Assert.Single(ranking.Top).DisplayName);
    }

    [Fact]
    public async Task Two_participants_cannot_share_an_alias_even_with_other_casing()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres");
        var beto = await AddMemberAsync(w, "Beto", "Rojas");
        await JoinAsync(w, ana, true, "Fuerza21");

        await Assert.ThrowsAsync<ConflictException>(() => JoinAsync(w, beto, true, "fuerza21"));

        // Quien ya lo tiene puede volver a guardarlo (idempotente).
        await JoinAsync(w, ana, true, "FUERZA21");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("un alias demasiado largo para el ranking")]
    [InlineData("<script>")]
    [InlineData("   ")]
    public async Task An_invalid_alias_is_rejected(string alias)
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres");

        await Assert.ThrowsAsync<ValidationDomainException>(() => JoinAsync(w, ana, true, alias));
    }

    // ---------- cómo se cuenta y se ordena ----------

    [Fact]
    public async Task Days_count_once_per_day_and_voided_entries_do_not_count()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres");
        await JoinAsync(w, ana, false, null);
        await AddVisitsAsync(w, ana, Day(1), Day(1), Day(2));
        await AddVisitsAsync(w, ana, voided: true, Day(3));

        var ranking = await GetAsync(w, ana);

        Assert.Equal(2, Assert.Single(ranking.Top).Days);
        Assert.Equal(2, ranking.Me.Days);
    }

    [Fact]
    public async Task Positions_follow_days_and_a_tie_goes_to_who_reached_that_count_first()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres");
        var beto = await AddMemberAsync(w, "Beto", "Rojas");
        var cami = await AddMemberAsync(w, "Cami", "Díaz");
        await AddVisitsAsync(w, ana, Day(1), Day(5), Day(9));       // 3 días, el tercero el 9
        await AddVisitsAsync(w, beto, Day(2), Day(3), Day(4));      // 3 días, el tercero el 4: llegó antes
        await AddVisitsAsync(w, cami, Day(1), Day(2), Day(3), Day(4));
        foreach (var m in new[] { ana, beto, cami })
        {
            await JoinAsync(w, m, false, null);
        }

        var ranking = await GetAsync(w, ana);

        Assert.Equal(["Cami D.", "Beto R.", "Ana T."], ranking.Top.Select(e => e.DisplayName).ToArray());
        Assert.Equal([1, 2, 3], ranking.Top.Select(e => e.Position).ToArray());
        Assert.Equal(3, ranking.Me.Position);
        Assert.Equal(3, ranking.Me.Days);
    }

    [Fact]
    public async Task Only_the_current_month_counts_in_the_top_and_the_previous_month_forms_the_podium()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres");
        var beto = await AddMemberAsync(w, "Beto", "Rojas");
        // Septiembre: Ana 3 días, Beto 1. Octubre: Beto 2, Ana 1. El último día de septiembre no cuenta en octubre.
        await AddVisitsAsync(w, ana, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 30), Day(2));
        await AddVisitsAsync(w, beto, new DateOnly(2026, 9, 10), Day(1), Day(4));
        await JoinAsync(w, ana, false, null);
        await JoinAsync(w, beto, false, null);

        var ranking = await GetAsync(w, ana);

        Assert.Equal((2026, 10), (ranking.Year, ranking.Month));
        Assert.Equal(["Beto R.", "Ana T."], ranking.Top.Select(e => e.DisplayName).ToArray());
        Assert.Equal((2026, 9), (ranking.PreviousYear, ranking.PreviousMonth));
        Assert.Equal(["Ana T.", "Beto R."], ranking.PreviousPodium.Select(e => e.DisplayName).ToArray());
        Assert.Equal(3, ranking.PreviousPodium[0].Days);
    }

    [Fact]
    public async Task The_top_is_ten_and_the_member_below_it_still_sees_the_own_position()
    {
        var w = await SeedAsync();
        for (var i = 0; i < 12; i++)
        {
            var m = await AddMemberAsync(w, $"Miembro{i:D2}", "Zeta");
            // Cada uno entrena (12 - i) días: el último queda 12.º con 1 día.
            await AddVisitsAsync(w, m, Enumerable.Range(1, 12 - i).Select(Day).ToArray());
            await JoinAsync(w, m, false, null);
        }

        var last = await MemberIdByNameAsync(w, "Miembro11");
        var ranking = await GetAsync(w, last);

        Assert.Equal(10, ranking.Top.Count);
        Assert.DoesNotContain(ranking.Top, e => e.IsMe);
        Assert.Equal(12, ranking.Me.Position);
        Assert.Equal(1, ranking.Me.Days);
    }

    [Fact]
    public async Task A_participant_without_days_this_month_has_no_position_and_is_not_listed()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres");
        await JoinAsync(w, ana, false, null);

        var ranking = await GetAsync(w, ana);

        Assert.Empty(ranking.Top);
        Assert.True(ranking.Me.Participating);
        Assert.Null(ranking.Me.Position);
        Assert.Equal(0, ranking.Me.Days);
    }

    [Fact]
    public async Task The_ranking_never_mixes_gyms()
    {
        var w1 = await SeedAsync();
        var w2 = await SeedAsync();
        var ana = await AddMemberAsync(w1, "Ana", "Torres");
        var otro = await AddMemberAsync(w2, "Otro", "Gym");
        await AddVisitsAsync(w1, ana, Day(1));
        await AddVisitsAsync(w2, otro, Day(1), Day(2));
        await JoinAsync(w1, ana, false, null);
        await JoinAsync(w2, otro, false, null);

        var ranking = await GetAsync(w1, ana);

        Assert.Equal("Ana T.", Assert.Single(ranking.Top).DisplayName);
    }

    // ---------- el gym lo apaga ----------

    [Fact]
    public async Task When_the_gym_turns_the_ranking_off_reading_and_joining_are_forbidden()
    {
        var w = await SeedAsync();
        var ana = await AddMemberAsync(w, "Ana", "Torres");
        await SetRankingFlagAsync(w, false);

        await Assert.ThrowsAsync<ForbiddenDomainException>(() => GetAsync(w, ana));
        await Assert.ThrowsAsync<ForbiddenDomainException>(() => JoinAsync(w, ana, false, null));

        await SetRankingFlagAsync(w, true);
        Assert.NotNull(await GetAsync(w, ana));
    }

    [Fact]
    public void The_ranking_flag_is_on_by_default_and_updating_flags_without_it_keeps_it()
    {
        var flags = GymFeatureFlags.Default(Guid.NewGuid());
        Assert.True(flags.ShowRanking);

        flags.Update(true, true, true, true, true, true, false, true, showRanking: false);
        Assert.False(flags.ShowRanking);

        flags.Update(true, true, true, true, true, true, false, true);
        Assert.False(flags.ShowRanking);

        flags.Update(true, true, true, true, true, true, false, true, showRanking: true);
        Assert.True(flags.ShowRanking);
    }

    // ---------- helpers ----------

    private static DateOnly Day(int dayOfOctober) => new(2026, 10, dayOfOctober);

    private IServiceScope ScopeAs(World w, Guid? userId = null)
    {
        var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(userId ?? Guid.CreateVersion7(), w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<RankingResponse> GetAsync(World w, Guid memberId)
    {
        using var scope = ScopeAs(w, w.UserOf[memberId]);
        return await scope.ServiceProvider.GetRequiredService<GetMyRankingHandler>().HandleAsync(default);
    }

    private async Task JoinAsync(World w, Guid memberId, bool useAlias, string? alias)
    {
        using var scope = ScopeAs(w, w.UserOf[memberId]);
        await scope.ServiceProvider.GetRequiredService<JoinRankingHandler>()
            .HandleAsync(new JoinRankingRequest(useAlias, alias), default);
    }

    private async Task LeaveAsync(World w, Guid memberId)
    {
        using var scope = ScopeAs(w, w.UserOf[memberId]);
        await scope.ServiceProvider.GetRequiredService<LeaveRankingHandler>().HandleAsync(default);
    }

    private Task AddVisitsAsync(World w, Guid memberId, params DateOnly[] days)
        => AddVisitsAsync(w, memberId, voided: false, days);

    private async Task AddVisitsAsync(World w, Guid memberId, bool voided, params DateOnly[] days)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        foreach (var day in days)
        {
            var entry = AttendanceEntity.Create(
                w.GymId, memberId, GSM.Domain.Attendance.AttendanceMethod.Manual,
                new DateTimeOffset(day.Year, day.Month, day.Day, 17, 0, 0, TimeSpan.Zero), day);
            if (voided)
            {
                entry.Void(Guid.CreateVersion7(), "Error de digitación", DateTimeOffset.UtcNow);
            }

            db.Add(entry);
        }

        await db.SaveChangesAsync();
    }

    private async Task<int> CountParticipantsAsync(World w)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await db.Set<RankingParticipant>().IgnoreQueryFilters().CountAsync(p => p.GymId == w.GymId);
    }

    private async Task DeactivateAsync(Guid memberId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var member = await db.Set<Member>().IgnoreQueryFilters().FirstAsync(m => m.Id == memberId);
        member.Deactivate();
        await db.SaveChangesAsync();
    }

    private async Task SetRankingFlagAsync(World w, bool on)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var flags = await db.Set<GymFeatureFlags>().IgnoreQueryFilters().FirstAsync(f => f.GymId == w.GymId);
        flags.Update(
            flags.ShowRoutines, flags.ShowClasses, flags.ShowProgress, flags.ShowGamification, flags.ShowCommunity,
            flags.CheckinQr, flags.MembersCanCreateRoutines, flags.MembersCanReportEquipment,
            showRanking: on);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> MemberIdByNameAsync(World w, string firstName)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await db.Set<Member>().IgnoreQueryFilters()
            .Where(m => m.GymId == w.GymId && m.FirstName == firstName)
            .Select(m => m.Id)
            .SingleAsync();
    }

    private async Task<Guid> AddMemberAsync(
        World w, string first, string last, string? photoUrl = null, DateOnly? birthDate = null,
        bool unknownBirthDate = false)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        // Sin fecha indicada = adulto; unknownBirthDate = ficha anterior a la v2 sin fecha de nacimiento.
        DateOnly? birth = unknownBirthDate ? null : birthDate ?? Today.AddYears(-30);
        var userId = Guid.CreateVersion7();
        var minor = birth is { } b && b > Today.AddYears(-18);
        var member = Member.Create(
            w.GymId,
            new MemberProfile(
                first, last, DocumentType.Cc, $"doc-{Guid.NewGuid():N}"[..12], birth, PhotoUrl: photoUrl,
                // Un menor exige los datos del acudiente.
                GuardianName: minor ? "Acudiente Prueba" : null,
                GuardianDocumentType: minor ? DocumentType.Cc : null,
                GuardianDocumentNumber: minor ? $"g-{Guid.NewGuid():N}"[..12] : null,
                GuardianPhone: minor ? "+573001112233" : null,
                GuardianRelationship: minor ? "Madre" : null),
            Today.AddDays(-400), today: Today);
        member.LinkUser(userId);
        db.Add(member);
        await db.SaveChangesAsync();

        w.UserOf[member.Id] = userId;
        return member.Id;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Ranking Gym", $"rk-{Guid.NewGuid():N}"[..16]);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        return new World(gym.Id);
    }

    private sealed record World(Guid GymId)
    {
        public Dictionary<Guid, Guid> UserOf { get; } = [];
    }
}
