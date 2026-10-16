using GSM.Application.Classes.Public;
using GSM.Application.Dashboard;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;
using AttMethod = GSM.Domain.Attendance.AttendanceMethod;

namespace GSM.IntegrationTests;

/// <summary>
/// D2: el inicio del miembro en una llamada (<c>GET /api/me/home</c>, Dashboards §2, §4 y §7). Un bloque apagado por
/// el gym o cuyo lector falla llega nulo y el resto se muestra; los días se cuentan en la zona del gym y los datos de
/// otro gym nunca aparecen.
/// </summary>
public sealed class MemberHomeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day = new(2026, 6, 15);

    private sealed record World(
        Guid GymId, Guid OwnerId, Guid MemberId, Guid MemberUserId, Guid ClassId, Guid SessionId);

    [Fact]
    public async Task Every_block_arrives_in_a_single_call()
    {
        var w = await SeedAsync();
        var clock = factory.Services.GetRequiredService<MutableClock>();
        var sixTwelve = clock.BusinessDayRange(Day).Start.AddHours(6).AddMinutes(12);

        await SeedMembershipEndingInAsync(w, daysLeft: 5);
        await SeedAttendanceAsync(w, sixTwelve, Day);
        await SeedAnnouncementAsync(w, "Cierre por festivo", pinned: true);
        await SeedBookingAsync(w);

        var home = await RunAsync(w, w.MemberUserId);

        // 1. Saludo y estado: por vencer, con el letrero y el plan que cubre hoy.
        Assert.Equal("expiring", home.Status!.Access.Commercial);
        Assert.False(string.IsNullOrWhiteSpace(home.Status.Access.Notice));
        Assert.NotNull(home.Status.PlanName);
        Assert.Equal(Day.AddDays(5), home.Status.EndDate);

        // 2. El gym ahora (sin horario cargado está siempre abierto; sin aforo no hay nivel).
        Assert.NotNull(home.Gym);
        Assert.True(home.Gym!.OpenStatus.IsOpen || !home.Gym.OpenStatus.HasSchedule);

        // 3. Entrada: "Entraste hoy a las 6:12".
        Assert.True(home.Entry!.CheckedInToday);
        Assert.Equal(sixTwelve, home.Entry.CheckedInAt);

        // 4. Pendientes: nada por confirmar ni reclamar, pero el bloque viaja con listas vacías.
        Assert.NotNull(home.Pending);
        Assert.Empty(home.Pending!.WaitlistOffers);
        Assert.Empty(home.Pending.Rewards);

        // 5. Sin rutina activa no hay bloque de entrenamiento.
        Assert.Null(home.Training);

        // 6. Próxima clase: la siguiente reserva.
        Assert.Equal(w.ClassId, home.NextClass!.ClassId);
        Assert.Equal(Day.AddDays(1), home.NextClass.SessionDate);

        // 7. Progreso.
        Assert.NotNull(home.Progress);
        Assert.True(home.Progress!.FreezesAvailable >= 0);

        // 8. Novedades: el fijado y uno sin leer.
        Assert.Equal("Cierre por festivo", home.News!.Featured!.Title);
        Assert.True(home.News.Featured.Pinned);
        Assert.Equal(1, home.News.UnreadAnnouncements);
    }

    [Fact]
    public async Task A_block_the_gym_turned_off_arrives_null_not_a_403()
    {
        var w = await SeedAsync();
        await SeedAnnouncementAsync(w, "Anuncio", pinned: true);
        await SeedBookingAsync(w);
        await UpdateFlagsAsync(w, routines: false, classes: false, gamification: false, community: false);

        var home = await RunAsync(w, w.MemberUserId);

        Assert.Null(home.Training);
        Assert.Null(home.NextClass);
        Assert.Null(home.News);
        Assert.NotNull(home.Status);
        Assert.NotNull(home.Gym);
        Assert.NotNull(home.Entry);
        Assert.NotNull(home.Pending);
        Assert.Empty(home.Pending!.WaitlistOffers);
        Assert.Null(home.Progress!.Ranking); // la gamificación apagada esconde el ranking, no la racha
    }

    [Fact]
    public async Task A_failing_reader_leaves_only_its_block_null()
    {
        var w = await SeedAsync();
        await SeedBookingAsync(w);

        using var scope = factory.CreateScopeWithMigration();
        Pin(scope, w, w.MemberUserId, null);
        var handler = ActivatorUtilities.CreateInstance<GetMyHomeHandler>(scope.ServiceProvider, new ThrowingClassReader());

        var home = await handler.HandleAsync(default);

        Assert.Null(home.NextClass);                // IClassReader falló
        Assert.NotNull(home.Pending);               // la parte de clases queda vacía; la de premios sigue
        Assert.Empty(home.Pending!.WaitlistOffers);
        Assert.NotNull(home.Status);
        Assert.NotNull(home.Gym);
        Assert.NotNull(home.Entry);
        Assert.NotNull(home.Progress);
        Assert.NotNull(home.News);
    }

    [Fact]
    public async Task The_entry_is_counted_by_business_day_at_8pm_Colombia_time()
    {
        var w = await SeedAsync();
        var clock = factory.Services.GetRequiredService<MutableClock>();
        var eightPm = clock.BusinessDayRange(Day).Start.AddHours(20); // 01:00 UTC del día siguiente
        var yesterdayMorning = clock.BusinessDayRange(Day.AddDays(-1)).Start.AddHours(7);

        await SeedAttendanceAsync(w, yesterdayMorning, Day.AddDays(-1));
        await SeedAttendanceAsync(w, eightPm, Day);

        var home = await RunAsync(w, w.MemberUserId, at: eightPm);

        Assert.True(home.Entry!.CheckedInToday);
        Assert.Equal(eightPm, home.Entry.CheckedInAt);
    }

    [Fact]
    public async Task Yesterdays_entry_does_not_count_as_today()
    {
        var w = await SeedAsync();
        var clock = factory.Services.GetRequiredService<MutableClock>();
        await SeedAttendanceAsync(w, clock.BusinessDayRange(Day.AddDays(-1)).Start.AddHours(7), Day.AddDays(-1));

        var home = await RunAsync(w, w.MemberUserId);

        Assert.False(home.Entry!.CheckedInToday);
        Assert.Null(home.Entry.CheckedInAt);
    }

    [Fact]
    public async Task An_open_waitlist_offer_shows_up_as_pending_until_it_expires()
    {
        var w = await SeedAsync();
        var clock = factory.Services.GetRequiredService<MutableClock>();
        var now = clock.BusinessDayRange(Day).Start.AddHours(9);
        await SeedOfferAsync(w, now, expiresInMinutes: 30);

        var open = await RunAsync(w, w.MemberUserId, at: now.AddMinutes(5));
        var offer = Assert.Single(open.Pending!.WaitlistOffers);
        Assert.Equal(w.ClassId, offer.ClassId);
        Assert.Equal(now.AddMinutes(30), offer.ExpiresAt);

        var expired = await RunAsync(w, w.MemberUserId, at: now.AddMinutes(45));
        Assert.Empty(expired.Pending!.WaitlistOffers);
    }

    [Fact]
    public async Task Another_gyms_member_never_sees_this_gyms_data()
    {
        var mine = await SeedAsync();
        var other = await SeedAsync();
        var clock = factory.Services.GetRequiredService<MutableClock>();
        await SeedAttendanceAsync(mine, clock.BusinessDayRange(Day).Start.AddHours(8), Day);
        await SeedAnnouncementAsync(mine, "Solo para mi gym", pinned: true);
        await SeedBookingAsync(mine);

        var home = await RunAsync(other, other.MemberUserId);

        Assert.False(home.Entry!.CheckedInToday);
        Assert.Null(home.NextClass);
        Assert.Null(home.News!.Featured);
        Assert.Equal(0, home.News.UnreadAnnouncements);
    }

    [Fact]
    public async Task An_account_not_linked_to_a_member_is_forbidden()
    {
        var w = await SeedAsync();

        using var scope = factory.CreateScopeWithMigration();
        Pin(scope, w, Guid.CreateVersion7(), null);

        await Assert.ThrowsAsync<ForbiddenDomainException>(
            () => scope.ServiceProvider.GetRequiredService<GetMyHomeHandler>().HandleAsync(default));
    }

    // ---------- helpers ----------

    private async Task<MemberHomeResponse> RunAsync(World w, Guid userId, DateTimeOffset? at = null)
    {
        using var scope = factory.CreateScopeWithMigration();
        Pin(scope, w, userId, at);
        return await scope.ServiceProvider.GetRequiredService<GetMyHomeHandler>().HandleAsync(default);
    }

    private static void Pin(IServiceScope scope, World w, Guid userId, DateTimeOffset? at)
    {
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, w.GymId, UserRole.Member);
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        if (at is { } instant)
        {
            clock.PinTo(instant, Day);
        }
        else
        {
            clock.PinToBusinessDay(Day);
        }
    }

    private async Task<World> SeedAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Home Gym", $"home-{Guid.NewGuid():N}"[..18]);
        var owner = User("owner");
        var memberUser = User("member");
        gym.AssignOwner(owner.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(owner);
        db.Add(memberUser);

        var member = Member.Create(gym.Id, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..14], Day);
        member.LinkUser(memberUser.Id);
        db.Add(member);

        // Una clase diaria, con su sesión de mañana ya materializada.
        var gymClass = GymClass.Create(gym.Id, Day.AddDays(-30), new GymClassDraft(
            "Spinning", null, null, 10, new TimeOnly(7, 0), 60, true,
            [ClassDayOfWeek.Monday, ClassDayOfWeek.Tuesday, ClassDayOfWeek.Wednesday, ClassDayOfWeek.Thursday,
             ClassDayOfWeek.Friday, ClassDayOfWeek.Saturday, ClassDayOfWeek.Sunday], null));
        db.Add(gymClass);
        var session = ClassSession.Materialize(gym.Id, gymClass.Id, Day.AddDays(1), new TimeOnly(7, 0), 60, null, 10);
        db.Add(session);

        await db.SaveChangesAsync();
        return new World(gym.Id, owner.Id, member.Id, memberUser.Id, gymClass.Id, session.Id);
    }

    private async Task SeedMembershipEndingInAsync(World w, int daysLeft)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var endDate = Day.AddDays(daysLeft);
        var plan = MembershipPlan.Create(w.GymId, $"Mensual {Guid.NewGuid():N}"[..20], null, 50000m, 30);
        db.AddRange(plan, Membership.Create(w.GymId, w.MemberId, plan.Id, endDate.AddDays(-29), 30));
        await db.SaveChangesAsync();
    }

    private async Task SeedAttendanceAsync(World w, DateTimeOffset at, DateOnly day)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        db.Add(AttendanceEntity.Create(w.GymId, w.MemberId, AttMethod.Manual, at, day));
        await db.SaveChangesAsync();
    }

    private async Task SeedAnnouncementAsync(World w, string title, bool pinned)
    {
        using var scope = System();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var announcement = Announcement.CreateDraft(w.GymId, w.OwnerId, title, "Texto del anuncio", pinned, null, false);
        announcement.Publish(clock.BusinessDayRange(Day).Start.AddHours(7), Day);
        db.Add(announcement);
        await db.SaveChangesAsync();
    }

    private async Task SeedBookingAsync(World w)
    {
        using var scope = System();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        db.Add(ClassBooking.Book(w.GymId, w.SessionId, w.MemberId, clock.BusinessDayRange(Day).Start.AddHours(8)));
        await db.SaveChangesAsync();
    }

    private async Task SeedOfferAsync(World w, DateTimeOffset now, int expiresInMinutes)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var entry = WaitlistEntry.Join(w.GymId, w.SessionId, w.MemberId, 1);
        entry.Offer(now, now.AddMinutes(expiresInMinutes));
        db.Add(entry);
        await db.SaveChangesAsync();
    }

    private async Task UpdateFlagsAsync(World w, bool routines, bool classes, bool gamification, bool community)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var flags = db.Set<GymFeatureFlags>().Single(f => f.GymId == w.GymId);
        flags.Update(routines, classes, true, gamification, community, true, false);
        await db.SaveChangesAsync();
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Day);
        return scope;
    }

    private static GSM.Infrastructure.Identity.AppUser User(string name)
    {
        var email = $"{name}-{Guid.NewGuid():N}@home.local";
        return new()
        {
            UserName = email,
            Email = email,
            NormalizedUserName = email.ToUpperInvariant(),
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = $"Home {name}",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
    }

    private sealed class ThrowingClassReader : IClassReader
    {
        public Task<int> GetAttendedCountAsync(Guid memberId, CancellationToken cancellationToken = default) => throw Boom();

        public Task<IReadOnlyList<UpcomingBookingView>> GetUpcomingBookingsAsync(Guid memberId, int limit, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<int> GetTodaySessionCountAsync(Guid gymId, CancellationToken cancellationToken = default) => throw Boom();

        public Task<IReadOnlyCollection<Guid>> GetMemberIdsByClassesAsync(IReadOnlyCollection<Guid> classIds, DateOnly since, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<string?> GetClassNameAsync(Guid classId, CancellationToken cancellationToken = default) => throw Boom();

        public Task<IReadOnlyList<TodaySessionView>> GetTodaySessionsAsync(Guid gymId, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<IReadOnlyList<WaitlistOfferView>> GetPendingWaitlistOffersAsync(Guid memberId, CancellationToken cancellationToken = default)
            => throw Boom();
    }

    private static InvalidOperationException Boom() => new("lector roto a propósito");
}
