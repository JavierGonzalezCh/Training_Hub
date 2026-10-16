using GSM.Application.Attendance;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using GSM.Application.Dashboard;
using GSM.Application.Members.Public;
using GSM.Application.Notifications;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Comunicaciones CO5: felicitación de cumpleaños. La tarea periódica decide a quién y publica
/// <c>BirthdayGreetingDue</c>; Notifications lo programa a las 8:00 (N5). El tiempo va por <c>MutableClock</c> y el
/// dispatcher se corre a mano.
/// </summary>
public sealed class BirthdayGreetingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Joined = new(2026, 1, 5);
    private static readonly DateOnly Birthday = new(2027, 3, 10);

    // ---------- La tarea ----------

    [Fact]
    public async Task The_job_greets_only_active_members_whose_birthday_is_today_and_is_idempotent()
    {
        var w = await SeedAsync(enabled: true);
        var today = await AddMemberAsync(w, "Ana", new DateOnly(1990, 3, 10));
        var tomorrow = await AddMemberAsync(w, "Beto", new DateOnly(1990, 3, 11));
        var inactive = await AddMemberAsync(w, "Cris", new DateOnly(1985, 3, 10), inactive: true);
        var noBirthDate = await AddMemberAsync(w, "Dani", null);

        await RunJobAtAsync(AtLocal(Birthday, 6), Birthday);
        await RunJobAtAsync(AtLocal(Birthday, 6, 10), Birthday);

        var notices = await NoticesAsync(w.GymId);
        var greeting = Assert.Single(notices);
        Assert.Equal(today, greeting.RecipientMemberId);
        Assert.Equal(NotificationCategory.News, greeting.Category);
        Assert.Equal(AtLocal(Birthday, 8), greeting.ScheduledFor);
        Assert.Equal("¡Feliz cumpleaños!", greeting.Title);
        Assert.Contains("Ana", greeting.Body);
        Assert.DoesNotContain(notices, n => n.RecipientMemberId == tomorrow
            || n.RecipientMemberId == inactive || n.RecipientMemberId == noBirthDate);
    }

    [Fact]
    public async Task The_greeting_reaches_the_inbox_at_8am_and_not_before()
    {
        var w = await SeedAsync(enabled: true);
        var ana = await AddMemberAsync(w, "Ana", new DateOnly(1990, 3, 10), userId: Guid.CreateVersion7());

        await RunJobAtAsync(AtLocal(Birthday, 6), Birthday);

        await RunDispatcherAtAsync(AtLocal(Birthday, 7, 59), Birthday, w.GymId);
        Assert.Equal(NotificationStatus.Pending, Assert.Single(await NoticesAsync(w.GymId)).Status);

        await RunDispatcherAtAsync(AtLocal(Birthday, 8), Birthday, w.GymId);
        Assert.Equal(NotificationStatus.Sent, Assert.Single(await NoticesAsync(w.GymId)).Status);
        Assert.NotEqual(Guid.Empty, ana);
    }

    [Fact]
    public async Task A_gym_with_the_greeting_off_publishes_nothing()
    {
        var w = await SeedAsync(enabled: false);
        await AddMemberAsync(w, "Ana", new DateOnly(1990, 3, 10));

        await RunJobAtAsync(AtLocal(Birthday, 6), Birthday);

        Assert.Empty(await NoticesAsync(w.GymId));
    }

    [Fact]
    public async Task A_gym_with_no_settings_row_defaults_to_off()
    {
        var w = await SeedAsync(enabled: null);
        await AddMemberAsync(w, "Ana", new DateOnly(1990, 3, 10));

        await RunJobAtAsync(AtLocal(Birthday, 6), Birthday);

        Assert.Empty(await NoticesAsync(w.GymId));
    }

    [Fact]
    public async Task A_job_that_runs_after_8am_sends_the_greeting_right_away()
    {
        var w = await SeedAsync(enabled: true);
        await AddMemberAsync(w, "Ana", new DateOnly(1990, 3, 10));
        var late = AtLocal(Birthday, 15);

        await RunJobAtAsync(late, Birthday);

        Assert.Equal(late, Assert.Single(await NoticesAsync(w.GymId)).ScheduledFor);
    }

    [Fact]
    public async Task The_gym_text_is_used_with_its_variables_and_the_default_otherwise()
    {
        var custom = await SeedAsync(enabled: true, message: "Feliz día, {nombre}, de parte de {gym}");
        await AddMemberAsync(custom, "Ana", new DateOnly(1990, 3, 10));
        var standard = await SeedAsync(enabled: true);
        await AddMemberAsync(standard, "Beto", new DateOnly(1990, 3, 10));

        await RunJobAtAsync(AtLocal(Birthday, 6), Birthday);

        Assert.Equal("Feliz día, Ana, de parte de CO5 Gym", Assert.Single(await NoticesAsync(custom.GymId)).Body);
        Assert.Equal(
            "¡Feliz cumpleaños, Beto! Hoy tu entrenamiento va por cuenta de la casa 🎉",
            Assert.Single(await NoticesAsync(standard.GymId)).Body);
    }

    // ---------- 29 de febrero ----------

    [Fact]
    public async Task Someone_born_on_feb_29_is_greeted_on_feb_28_in_common_years_and_on_feb_29_in_leap_years()
    {
        var w = await SeedAsync(enabled: true);
        await AddMemberAsync(w, "Bisiesta", new DateOnly(2000, 2, 29));

        // 2027 no es bisiesto: el 28 sí, el 1 de marzo no.
        await RunJobAtAsync(AtLocal(new DateOnly(2027, 2, 28), 6), new DateOnly(2027, 2, 28));
        Assert.Single(await NoticesAsync(w.GymId));
        await RunJobAtAsync(AtLocal(new DateOnly(2027, 3, 1), 6), new DateOnly(2027, 3, 1));
        Assert.Single(await NoticesAsync(w.GymId));

        // 2028 es bisiesto: el 28 no, el 29 sí.
        await RunJobAtAsync(AtLocal(new DateOnly(2028, 2, 28), 6), new DateOnly(2028, 2, 28));
        Assert.Single(await NoticesAsync(w.GymId));
        await RunJobAtAsync(AtLocal(new DateOnly(2028, 2, 29), 6), new DateOnly(2028, 2, 29));
        Assert.Equal(2, (await NoticesAsync(w.GymId)).Count);
    }

    [Fact]
    public async Task On_feb_28_of_a_common_year_both_feb_28_and_feb_29_births_are_greeted()
    {
        var w = await SeedAsync(enabled: true);
        await AddMemberAsync(w, "Veintiocho", new DateOnly(1995, 2, 28));
        await AddMemberAsync(w, "Veintinueve", new DateOnly(1996, 2, 29));

        await RunJobAtAsync(AtLocal(new DateOnly(2027, 2, 28), 6), new DateOnly(2027, 2, 28));

        Assert.Equal(2, (await NoticesAsync(w.GymId)).Count);
    }

    // ---------- Ajustes ----------

    [Fact]
    public async Task Birthday_settings_default_to_off_and_the_default_text_then_persist()
    {
        var w = await SeedAsync(enabled: null);

        using (var scope = AsOwner(w, AtLocal(Birthday, 12)))
        {
            var initial = await scope.ServiceProvider.GetRequiredService<GetBirthdaySettingsHandler>().HandleAsync(default);
            Assert.False(initial.Enabled);
            Assert.True(initial.IsDefaultMessage);
            Assert.Equal(CommunicationsSettings.DefaultBirthdayMessage, initial.Message);
            Assert.Contains("{nombre}", initial.Variables);
        }

        using (var scope = AsOwner(w, AtLocal(Birthday, 12)))
        {
            var saved = await scope.ServiceProvider.GetRequiredService<UpdateBirthdaySettingsHandler>()
                .HandleAsync(new UpdateBirthdaySettingsRequest(true, "  Feliz cumple, {nombre}  "), default);
            Assert.True(saved.Enabled);
            Assert.False(saved.IsDefaultMessage);
            Assert.Equal("Feliz cumple, {nombre}", saved.Message);
        }

        using (var scope = AsOwner(w, AtLocal(Birthday, 12)))
        {
            var again = await scope.ServiceProvider.GetRequiredService<GetBirthdaySettingsHandler>().HandleAsync(default);
            Assert.True(again.Enabled);
            Assert.Equal("Feliz cumple, {nombre}", again.Message);

            var reset = await scope.ServiceProvider.GetRequiredService<UpdateBirthdaySettingsHandler>()
                .HandleAsync(new UpdateBirthdaySettingsRequest(true, "   "), default);
            Assert.True(reset.IsDefaultMessage);
        }
    }

    [Fact]
    public async Task A_birthday_text_over_the_limit_is_a_domain_validation_error()
    {
        var w = await SeedAsync(enabled: null);
        using var scope = AsOwner(w, AtLocal(Birthday, 12));

        await Assert.ThrowsAsync<ValidationDomainException>(() => scope.ServiceProvider
            .GetRequiredService<UpdateBirthdaySettingsHandler>()
            .HandleAsync(new UpdateBirthdaySettingsRequest(
                true, new string('x', CommunicationsSettings.BirthdayMessageMaxLength + 1)), default));
    }

    // ---------- Panel ----------

    [Fact]
    public async Task The_dashboard_lists_todays_birthdays_even_with_the_greeting_off()
    {
        var w = await SeedAsync(enabled: false);
        var ana = await AddMemberAsync(w, "Ana", new DateOnly(1990, 3, 10));
        await AddMemberAsync(w, "Beto", new DateOnly(1990, 3, 11));

        using var scope = AsOwner(w, AtLocal(Birthday, 9));
        var dashboard = await scope.ServiceProvider.GetRequiredService<GetDashboardHandler>().HandleAsync(default);

        var row = Assert.Single(dashboard.Today!.Birthdays!);
        Assert.Equal(ana, row.MemberId);
        Assert.Contains("Ana", row.MemberName);
    }

    [Fact]
    public async Task The_reception_card_flags_a_birthday_only_on_the_day()
    {
        var w = await SeedAsync(enabled: false);
        var ana = await AddMemberAsync(w, "Ana", new DateOnly(1990, 3, 10));

        using (var scope = AsOwner(w, AtLocal(Birthday, 9)))
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<GetReceptionCardHandler>()
                .HandleAsync(ana, default)).BirthdayToday);
        }

        using (var scope = AsOwner(w, AtLocal(Birthday.AddDays(1), 9)))
        {
            Assert.False((await scope.ServiceProvider.GetRequiredService<GetReceptionCardHandler>()
                .HandleAsync(ana, default)).BirthdayToday);
        }
    }

    [Fact]
    public async Task The_member_reader_scopes_birthdays_to_the_gym()
    {
        var mine = await SeedAsync(enabled: false);
        var other = await SeedAsync(enabled: false);
        var ana = await AddMemberAsync(mine, "Ana", new DateOnly(1990, 3, 10));
        await AddMemberAsync(other, "Otra", new DateOnly(1990, 3, 10));

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var found = await scope.ServiceProvider.GetRequiredService<IMemberReader>()
            .GetBirthdaysOnAsync(mine.GymId, Birthday, default);

        Assert.Equal(ana, Assert.Single(found).Id);
    }

    // ---------- helpers ----------

    private async Task RunJobAtAsync(DateTimeOffset instant, DateOnly day)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(instant, day);
        await scope.ServiceProvider.GetRequiredService<RunCommunicationsJobHandler>().HandleAsync(default);
    }

    private async Task RunDispatcherAtAsync(DateTimeOffset instant, DateOnly day, Guid gymId)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(instant, day);
        await scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>().RunOnceAsync(onlyGymId: gymId);
    }

    private async Task<List<Notification>> NoticesAsync(Guid gymId)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .AsNoTracking()
            .Where(n => n.GymId == gymId && n.Type == NotificationType.Birthday)
            .OrderBy(n => n.ScheduledFor)
            .ToListAsync();
    }

    private static DateTimeOffset AtLocal(DateOnly day, int hour, int minute = 0)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(hour).AddMinutes(minute);

    private static DateOnly DayOf(DateTimeOffset instant)
        => DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope AsOwner(World w, DateTimeOffset at)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), w.GymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(at, DayOf(at));
        return scope;
    }

    /// <param name="enabled">Nulo = sin fila de ajustes.</param>
    private async Task<World> SeedAsync(bool? enabled, string? message = null)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("CO5 Gym", $"co5-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        if (enabled is { } on)
        {
            var settings = CommunicationsSettings.Default(gym.Id);
            settings.UpdateBirthday(on, message);
            db.Add(settings);
        }

        await db.SaveChangesAsync();
        return new World(gym.Id);
    }

    private async Task<Guid> AddMemberAsync(
        World w, string firstName, DateOnly? birthDate, bool inactive = false, Guid? userId = null)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var member = Member.Create(
            w.GymId, firstName, "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Joined,
            email: $"co5-{Guid.NewGuid():N}@test.local", birthDate: birthDate);
        if (userId is { } id)
        {
            member.LinkUser(id);
        }

        if (inactive)
        {
            member.Deactivate();
        }

        db.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    private sealed record World(Guid GymId);
}
