using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Events;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Classes;
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
/// C8: aviso de clase nueva. Crear una clase (con "Avisar a los miembros" marcado, el valor por defecto) publica en
/// Novedades "Nueva clase: ..." y crea un aviso por miembro activo; editar nunca avisa; desmarcar la casilla no
/// publica nada; el evento repetido no duplica; quien apagó Novedades no recibe el push pero sí ve la publicación.
/// El recordatorio de 10 minutos ya lo cubren <c>ScheduledNoticeTests</c> (N5) y <c>ClassSelfAttendanceTests</c> (C7).
/// </summary>
public sealed class ClassCreatedNoticeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 3, 2);

    [Fact]
    public async Task Creating_a_class_publishes_a_news_announcement_and_a_notice_for_each_active_member()
    {
        var w = await SeedAsync(2, withInactiveMember: true);

        var classId = await CreateClassAsync(w, notify: true);

        var announcement = Assert.Single(await AnnouncementsAsync(w.GymId));
        Assert.Equal(AnnouncementStatus.Published, announcement.Status);
        Assert.Equal(AnnouncementCategory.GymNews, announcement.Category);
        Assert.Equal("Nueva clase: Yoga restaurativo", announcement.Title);
        Assert.Contains("martes y jueves", announcement.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("con Laura", announcement.Body);

        // Solo miembros activos: el dado de baja no recibe aviso.
        Assert.Equal(2, await CountNoticesAsync(w.GymId, NotificationType.Announcement));
        Assert.NotEqual(Guid.Empty, classId);
    }

    [Fact]
    public async Task Unchecking_notify_members_publishes_nothing()
    {
        var w = await SeedAsync(1);

        await CreateClassAsync(w, notify: false);

        Assert.Empty(await AnnouncementsAsync(w.GymId));
        Assert.Equal(0, await CountNoticesAsync(w.GymId, NotificationType.Announcement));
    }

    [Fact]
    public async Task Editing_a_class_never_announces()
    {
        var w = await SeedAsync(1);
        var classId = await CreateClassAsync(w, notify: false);

        using (var scope = AsOwner(w))
        {
            // Aunque la petición traiga NotifyMembers = true, editar no avisa.
            await scope.ServiceProvider.GetRequiredService<UpdateClassHandler>().HandleAsync(
                classId, Request(notify: true, name: "Yoga restaurativo II"), default);
        }

        Assert.Empty(await AnnouncementsAsync(w.GymId));
        Assert.Equal(0, await CountNoticesAsync(w.GymId, NotificationType.Announcement));
    }

    [Fact]
    public async Task Repeating_the_event_does_not_duplicate_the_announcement_or_the_notices()
    {
        var w = await SeedAsync(2);
        var classId = Guid.CreateVersion7();
        var evt = new ClassCreated(
            w.GymId, classId, "Zumba", null, new TimeOnly(18, 0), true, [ClassDayOfWeek.Friday], null, Guid.CreateVersion7());

        await PublishAsync(w, evt);
        await PublishAsync(w, evt);

        Assert.Single(await AnnouncementsAsync(w.GymId));
        Assert.Equal(2, await CountNoticesAsync(w.GymId, NotificationType.Announcement));
    }

    [Fact]
    public async Task A_member_who_turned_off_news_push_gets_no_push_but_the_announcement_stays_in_the_feed()
    {
        var w = await SeedAsync(2);
        using (var scope = AsMember(w, 0))
        {
            await scope.ServiceProvider.GetRequiredService<UpdateMyNotificationPreferencesHandler>().HandleAsync(
                new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdate("News", false, false)]), default);
        }

        await CreateClassAsync(w, notify: true);

        // La publicación es del gym: sigue en el feed de todos, también del que apagó el push.
        var announcement = Assert.Single(await AnnouncementsAsync(w.GymId));
        Assert.Equal(AnnouncementStatus.Published, announcement.Status);

        using var scopeDb = System();
        var db = scopeDb.ServiceProvider.GetRequiredService<GsmDbContext>();
        var mutedPushes = await db.Set<NotificationDelivery>().CountAsync(d =>
            d.Channel == DeliveryChannel.Push && d.Status == DeliveryStatus.Pending
            && db.Set<Notification>().Any(n => n.Id == d.NotificationId && n.RecipientMemberId == w.MemberIds[0]));
        Assert.Equal(0, mutedPushes);
    }

    // ---------- siembra y ayudas ----------

    private static SaveClassRequest Request(bool notify, string name = "Yoga restaurativo")
        => new(
            name, null, "Laura", 15, new TimeOnly(19, 0), 60,
            IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Tuesday, ClassDayOfWeek.Thursday], ScheduledDate: null,
            NotifyMembers: notify);

    private async Task<Guid> CreateClassAsync(World w, bool notify)
    {
        using var scope = AsOwner(w);
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
            .HandleAsync(Request(notify), default);
        return created.Id;
    }

    private async Task PublishAsync(World w, ClassCreated evt)
    {
        using var scope = AsOwner(w);
        await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().PublishAsync(evt, default);
    }

    private async Task<List<Announcement>> AnnouncementsAsync(Guid gymId)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Announcement>().AsNoTracking()
            .Where(a => a.GymId == gymId).ToListAsync();
    }

    private async Task<int> CountNoticesAsync(Guid gymId, NotificationType type)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .CountAsync(n => n.GymId == gymId && n.Type == type);
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private static DateTimeOffset NoonLocal()
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(Today).Start.AddHours(12);

    private IServiceScope AsOwner(World w)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.OwnerUserId, w.GymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(NoonLocal(), Today);
        return scope;
    }

    private IServiceScope AsMember(World w, int index)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserIds[index], w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(NoonLocal(), Today);
        return scope;
    }

    private async Task<World> SeedAsync(int members, bool withInactiveMember = false)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("C8 Gym", $"c8-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));

        var memberIds = new List<Guid>();
        var memberUserIds = new List<Guid>();
        for (var i = 0; i < members + (withInactiveMember ? 1 : 0); i++)
        {
            var userId = Guid.CreateVersion7();
            var member = Member.Create(
                gym.Id, "Miembro", $"{i}", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-60),
                email: $"c8-{Guid.NewGuid():N}@test.local");
            member.LinkUser(userId);
            if (withInactiveMember && i == members)
            {
                member.Deactivate();
            }
            else
            {
                memberIds.Add(member.Id);
                memberUserIds.Add(userId);
            }

            db.Add(member);
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, Guid.CreateVersion7(), memberIds, memberUserIds);
    }

    private sealed record World(
        Guid GymId, Guid OwnerUserId, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> MemberUserIds);
}
