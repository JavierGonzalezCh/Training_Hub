using System.Net;
using GSM.Application.Achievements.Events;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Events;
using GSM.Application.Communications;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Achievements;
using GSM.Domain.Classes;
using GSM.Domain.Communications;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Notifications;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Etapas 13.3 y 13.4 sobre Notifications (N1): avisos por evento, entregas por correo, preferencias y la
/// tarea periódica de recordatorios y purga.
/// </summary>
public sealed class CommunicationsNotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    // ---------- 13.3 · avisos por evento ----------

    [Fact]
    public async Task Cancelling_a_session_notifies_each_booked_member_once_even_if_the_event_repeats()
    {
        var world = await SeedAsync(3);
        var classId = await SeedClassAsync(world.GymId, "Spinning");
        var evt = new ClassSessionCancelled(classId, world.GymId, Today.AddDays(2), world.Members.Select(m => m.Id).ToList());

        await PublishAsStaffAsync(world.GymId, evt);
        await PublishAsStaffAsync(world.GymId, evt);

        Assert.Equal(3, await CountNotificationsAsync(world.GymId, NotificationType.ClassCancelled));

        using var scope = Member(world.GymId, world.Members[0].UserId);
        var list = await scope.ServiceProvider.GetRequiredService<ListMyNotificationsHandler>().HandleAsync(1, 20, default);
        var item = Assert.Single(list.Items);
        Assert.Contains("Spinning", item.Title);
        Assert.Equal("/app/clases", item.Link);
    }

    [Fact]
    public async Task Unlocking_an_achievement_creates_an_in_app_notice_without_email()
    {
        var world = await SeedAsync(1);
        var achievementId = await SeedAchievementAsync(world.GymId, "Constante");
        var evt = new AchievementUnlockedEvent(Guid.CreateVersion7(), world.Members[0].Id, world.GymId, achievementId);

        await PublishAsStaffAsync(world.GymId, evt);

        using (var scope = Member(world.GymId, world.Members[0].UserId))
        {
            var item = Assert.Single((await scope.ServiceProvider.GetRequiredService<ListMyNotificationsHandler>()
                .HandleAsync(1, 20, default)).Items);
            Assert.Equal("Desbloqueaste «Constante»", item.Title);
        }

        Assert.Equal(0, await CountDeliveriesAsync(world.GymId));
    }

    [Fact]
    public async Task Read_all_clears_the_unread_counter_and_others_notifications_are_not_found()
    {
        var world = await SeedAsync(2);
        var classId = await SeedClassAsync(world.GymId, "Yoga");
        await PublishAsStaffAsync(world.GymId,
            new ClassSessionCancelled(classId, world.GymId, Today, world.Members.Select(m => m.Id).ToList()));

        using (var scope = Member(world.GymId, world.Members[0].UserId))
        {
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<GetUnreadCountHandler>().HandleAsync(default)).Notifications);
            await scope.ServiceProvider.GetRequiredService<MarkAllNotificationsReadHandler>().HandleAsync(default);
        }

        using (var scope = Member(world.GymId, world.Members[0].UserId))
        {
            Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<GetUnreadCountHandler>().HandleAsync(default)).Notifications);
        }

        // La notificación del segundo miembro no se puede marcar desde la cuenta del primero.
        Guid othersId;
        using (var scope = Member(world.GymId, world.Members[1].UserId))
        {
            othersId = (await scope.ServiceProvider.GetRequiredService<ListMyNotificationsHandler>().HandleAsync(1, 20, default)).Items[0].Id;
        }

        using (var scope = Member(world.GymId, world.Members[0].UserId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
                .GetRequiredService<MarkNotificationReadHandler>().HandleAsync(othersId, default));
        }
    }

    // ---------- 13.4 · cola de correos, preferencias y tarea ----------

    [Fact]
    public async Task Publishing_by_email_queues_only_active_members_with_email_and_without_opt_out()
    {
        // 5 activos: 4 con email (uno con baja) y 1 sin email; más 1 inactivo con email → 3 correos.
        var world = await SeedAsync(4, withoutEmail: 1, inactive: 1);
        await OptOutAsync(world.GymId, world.Members[0].UserId);

        Guid withEmail, withoutEmail;
        using (var scope = Staff(world.GymId))
        {
            var create = scope.ServiceProvider.GetRequiredService<CreateAnnouncementHandler>();
            withEmail = (await create.HandleAsync(new CreateAnnouncementRequest("Festivo", "Cerramos el lunes.", false, null, true), default)).Id;
            withoutEmail = (await create.HandleAsync(new CreateAnnouncementRequest("Sin correo", "Solo en la app.", false, null, false), default)).Id;
        }

        using (var scope = Staff(world.GymId))
        {
            Assert.Equal(3, (await scope.ServiceProvider.GetRequiredService<GetEmailAudienceHandler>().HandleAsync(AnnouncementCategory.GymNews, default)).Recipients);
            var publish = scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>();
            var published = await publish.HandleAsync(withEmail, default);
            Assert.Equal(new EmailDelivery(3, 0, 0), published.Delivery);
        }

        using (var scope = Staff(world.GymId))
        {
            await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(withoutEmail, default);
        }

        Assert.Equal(3, await CountDeliveriesAsync(world.GymId));
    }

    [Fact]
    public async Task The_dispatcher_sends_pending_emails_once_and_retries_failures_up_to_three_times()
    {
        var world = await SeedAsync(2);
        var sender = factory.Services.GetRequiredService<CapturingEmailSender>();

        using (var scope = Staff(world.GymId))
        {
            var id = (await scope.ServiceProvider.GetRequiredService<CreateAnnouncementHandler>()
                .HandleAsync(new CreateAnnouncementRequest("Máquina nueva", "Llegó la prensa.", false, null, true), default)).Id;
            await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
        }

        sender.Clear();
        await RunDispatcherAsync();
        var mine = sender.Sent.Where(m => world.Members.Any(x => x.Email == m.To)).ToList();
        Assert.Equal(2, mine.Count);
        Assert.Contains("/api/public/unsubscribe?t=", mine[0].HtmlBody);

        sender.Clear();
        await RunDispatcherAsync();
        Assert.DoesNotContain(sender.Sent, m => world.Members.Any(x => x.Email == m.To));
        Assert.Equal(2, await CountDeliveriesAsync(world.GymId, DeliveryStatus.Sent));

        // Un correo que falla siempre queda Failed al tercer intento, con espera creciente entre ellos.
        Guid failingId, noticeId;
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var notice = Notification.CreateForMember(
                world.GymId, world.Members[0].Id, NotificationType.ClassCancelled, NotificationCategory.Classes,
                Guid.CreateVersion7(), "x", "x", null, NowOf(Today));
            var delivery = NotificationDelivery.CreateEmail(
                world.GymId, notice.Id, "falla@test.local", "x", "x", "x");
            db.AddRange(notice, delivery);
            await db.SaveChangesAsync();
            failingId = delivery.Id;
            noticeId = notice.Id;
        }

        await RunDispatcherAsync(failFor: "falla@test.local");
        await AssertDeliveryAsync(failingId, DeliveryStatus.Pending, 1);

        // Aún dentro de la espera (2 min): no reintenta.
        await RunDispatcherAsync(failFor: "falla@test.local");
        await AssertDeliveryAsync(failingId, DeliveryStatus.Pending, 1);

        await RunDispatcherAsync(failFor: "falla@test.local", minutesLater: 3);
        await AssertDeliveryAsync(failingId, DeliveryStatus.Pending, 2);

        await RunDispatcherAsync(failFor: "falla@test.local", minutesLater: 10);
        await AssertDeliveryAsync(failingId, DeliveryStatus.Failed, 3);

        using (var scope = System())
        {
            var notice = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
                .SingleAsync(n => n.Id == noticeId);
            Assert.Equal(NotificationStatus.Failed, notice.Status);
        }
    }

    [Fact]
    public async Task Expiring_reminders_are_created_once_skip_renewals_and_respect_the_gym_setting()
    {
        var world = await SeedAsync(3);
        await SeedMembershipEndingAsync(world.GymId, world.Members[0].Id, Today.AddDays(2));
        await SeedMembershipEndingAsync(world.GymId, world.Members[1].Id, Today.AddDays(2));
        await SeedMembershipEndingAsync(world.GymId, world.Members[1].Id, Today.AddDays(32)); // ya renovó
        await SeedMembershipEndingAsync(world.GymId, world.Members[2].Id, Today.AddDays(10)); // fuera de la ventana

        await RunJobAsync();
        await RunJobAsync();

        Assert.Equal(1, await CountNotificationsAsync(world.GymId, NotificationType.MembershipExpiring));
        Assert.Equal(1, await CountDeliveriesAsync(world.GymId, type: NotificationType.MembershipExpiring));

        // Con 0 días el gym desactiva los recordatorios.
        var other = await SeedAsync(1);
        await SeedMembershipEndingAsync(other.GymId, other.Members[0].Id, Today.AddDays(1));
        using (var scope = Owner(other.GymId))
        {
            await scope.ServiceProvider.GetRequiredService<UpdateCommunicationsSettingsHandler>()
                .HandleAsync(new UpdateCommunicationsSettingsRequest(0), default);
        }

        await RunJobAsync();
        Assert.Equal(0, await CountNotificationsAsync(other.GymId, NotificationType.MembershipExpiring));
    }

    [Fact]
    public async Task The_unsubscribe_link_opts_the_member_out_and_a_tampered_token_is_rejected()
    {
        var world = await SeedAsync(1);
        var member = world.Members[0];

        string url;
        using (var scope = System())
        {
            url = scope.ServiceProvider.GetRequiredService<IUnsubscribeLinks>()
                .BuildUrl(member.Id, world.GymId, NotificationCategory.News);
        }

        var token = Uri.UnescapeDataString(url[(url.IndexOf("t=", StringComparison.Ordinal) + 2)..]);
        var client = factory.CreateClient();

        var tampered = await client.GetAsync($"/api/public/unsubscribe?t={Uri.EscapeDataString(token[..^2] + "xx")}");
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);

        var ok = await client.GetAsync($"/api/public/unsubscribe?t={Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // El enlace apaga el correo de esa categoría (Novedades), no el de las demás.
        using var check = Member(world.GymId, member.UserId);
        var prefs = (await check.ServiceProvider.GetRequiredService<GetMyNotificationPreferencesHandler>().HandleAsync(default)).Items;
        Assert.False(prefs.Single(p => p.Category == "News").Email);
        Assert.True(prefs.Single(p => p.Category == "Classes").Email);
    }

    [Fact]
    public async Task The_job_endpoint_requires_the_secret_and_purges_old_notifications()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/internal/jobs/communications/run", null)).StatusCode);

        var wrong = new HttpRequestMessage(HttpMethod.Post, "/internal/jobs/communications/run");
        wrong.Headers.Add("X-Job-Secret", "no-es-el-secreto");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);

        var right = new HttpRequestMessage(HttpMethod.Post, "/internal/jobs/communications/run");
        right.Headers.Add("X-Job-Secret", "integration-tests-jobs-secret-32-bytes-min!!");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(right)).StatusCode);

        // Purga: leído hace 91 días se borra; leído hace 89 se conserva.
        var world = await SeedAsync(1);
        Guid oldId, recentId;
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var old = Notification.CreateForMember(world.GymId, world.Members[0].Id, NotificationType.AchievementUnlocked, NotificationCategory.Progress, Guid.CreateVersion7(), "Viejo", "", null, NowOf(Today));
            var recent = Notification.CreateForMember(world.GymId, world.Members[0].Id, NotificationType.AchievementUnlocked, NotificationCategory.Progress, Guid.CreateVersion7(), "Reciente", "", null, NowOf(Today));
            old.MarkRead(NowOf(Today));
            recent.MarkRead(NowOf(Today));
            db.AddRange(old, recent);
            await db.SaveChangesAsync();
            oldId = old.Id;
            recentId = recent.Id;
            var now = DateTimeOffset.UtcNow;
            await db.Set<Notification>().Where(n => n.Id == oldId).ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, now.AddDays(-91)));
            await db.Set<Notification>().Where(n => n.Id == recentId).ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, now.AddDays(-89)));
        }

        using (var scope = System())
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();
            await scope.ServiceProvider.GetRequiredService<PurgeNotificationsHandler>().HandleAsync(default);
        }

        using (var scope = System())
        {
            var ids = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
                .Where(n => n.RecipientMemberId == world.Members[0].Id).Select(n => n.Id).ToListAsync();
            Assert.DoesNotContain(oldId, ids);
            Assert.Contains(recentId, ids);
        }
    }

    // ---------- helpers ----------

    private static DateTimeOffset NowOf(DateOnly day)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(12);

    /// <summary>La tarea de comunicaciones: ahora solo publica los recordatorios como eventos.</summary>
    private async Task RunJobAsync()
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        await scope.ServiceProvider.GetRequiredService<RunCommunicationsJobHandler>().HandleAsync(default);
    }

    /// <summary>Una vuelta del dispatcher, a mano. <paramref name="minutesLater"/> adelanta el reloj.</summary>
    private async Task RunDispatcherAsync(string? failFor = null, int minutesLater = 0)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(NowOf(Today).AddMinutes(minutesLater), Today);
        var sender = factory.Services.GetRequiredService<CapturingEmailSender>();
        if (failFor is null)
        {
            await scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>().RunOnceAsync();
            return;
        }

        // Un remitente que falla solo para una dirección; el resto se comporta como el de captura.
        var runner = ActivatorUtilities.CreateInstance<NotificationDispatchRunner>(
            scope.ServiceProvider, (GSM.Application.Common.Abstractions.IEmailSender)new FailingFor(failFor, sender));
        await runner.RunOnceAsync();
    }

    private async Task AssertDeliveryAsync(Guid id, DeliveryStatus status, int attempts)
    {
        using var scope = System();
        var delivery = await scope.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<NotificationDelivery>().SingleAsync(d => d.Id == id);
        Assert.Equal(status, delivery.Status);
        Assert.Equal(attempts, delivery.Attempts);
    }

    private sealed class FailingFor(string address, CapturingEmailSender inner) : GSM.Application.Common.Abstractions.IEmailSender
    {
        public Task SendAsync(GSM.Application.Common.Abstractions.EmailMessage message, CancellationToken cancellationToken = default)
            => message.To == address
                ? throw new InvalidOperationException("Resend no disponible")
                : inner.SendAsync(message, cancellationToken);
    }

    private async Task PublishAsStaffAsync<TEvent>(Guid gymId, TEvent evt) where TEvent : IDomainEvent
    {
        using var scope = Staff(gymId);
        await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().PublishAsync(evt, default);
    }

    private async Task OptOutAsync(Guid gymId, Guid memberUserId)
    {
        using var scope = Member(gymId, memberUserId);
        await scope.ServiceProvider.GetRequiredService<UpdateMyNotificationPreferencesHandler>()
            .HandleAsync(new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdate("News", true, false)]), default);
    }

    private async Task<int> CountNotificationsAsync(Guid gymId, NotificationType type)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .CountAsync(n => n.GymId == gymId && n.Type == type);
    }

    private async Task<int> CountDeliveriesAsync(Guid gymId, DeliveryStatus? status = null, NotificationType? type = null)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await (
            from d in db.Set<NotificationDelivery>()
            join n in db.Set<Notification>() on d.NotificationId equals n.Id
            where d.GymId == gymId && (status == null ? d.Status != DeliveryStatus.Skipped : d.Status == status)
                && (type == null || n.Type == type)
            select d.Id).CountAsync();
    }

    private async Task<Guid> SeedClassAsync(Guid gymId, string name)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gymClass = GymClass.Create(gymId, Today.AddDays(-30), new GymClassDraft(
            name, null, null, 10, new TimeOnly(7, 0), 60, true,
            [ClassDayOfWeek.Monday, ClassDayOfWeek.Tuesday, ClassDayOfWeek.Wednesday, ClassDayOfWeek.Thursday,
             ClassDayOfWeek.Friday, ClassDayOfWeek.Saturday, ClassDayOfWeek.Sunday], null));
        db.Add(gymClass);
        await db.SaveChangesAsync();
        return gymClass.Id;
    }

    private async Task<Guid> SeedAchievementAsync(Guid gymId, string name)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var achievement = Achievement.Create(gymId, name, null, null, AchievementConditionType.Visits, 10);
        db.Add(achievement);
        await db.SaveChangesAsync();
        return achievement.Id;
    }

    private async Task SeedMembershipEndingAsync(Guid gymId, Guid memberId, DateOnly endDate)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var plan = MembershipPlan.Create(gymId, $"Mensual {Guid.NewGuid():N}"[..20], null, 50000m, 30);
        db.AddRange(plan, Membership.Create(gymId, memberId, plan.Id, endDate.AddDays(-29), 30));
        await db.SaveChangesAsync();
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Staff(Guid gymId) => Scope(gymId, Guid.CreateVersion7(), UserRole.Staff);

    private IServiceScope Owner(Guid gymId) => Scope(gymId, Guid.CreateVersion7(), UserRole.Owner);

    private IServiceScope Member(Guid gymId, Guid userId) => Scope(gymId, userId, UserRole.Member);

    private IServiceScope Scope(Guid gymId, Guid userId, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<World> SeedAsync(int withEmail, int withoutEmail = 0, int inactive = 0)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("Comms Gym", $"cn-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));

        var people = new List<Person>();
        void Add(bool email, bool active)
        {
            var userId = Guid.CreateVersion7();
            var address = email ? $"m-{Guid.NewGuid():N}@test.local" : null;
            var member = GSM.Domain.Members.Member.Create(
                gym.Id, "Miembro", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-60), email: address);
            member.LinkUser(userId);
            if (!active)
            {
                member.Deactivate();
            }

            db.Add(member);
            people.Add(new Person(member.Id, userId, address));
        }

        for (var i = 0; i < withEmail; i++) Add(true, true);
        for (var i = 0; i < withoutEmail; i++) Add(false, true);
        for (var i = 0; i < inactive; i++) Add(true, false);

        await db.SaveChangesAsync();
        return new World(gym.Id, people);
    }

    private sealed record Person(Guid Id, Guid UserId, string? Email);

    private sealed record World(Guid GymId, IReadOnlyList<Person> Members);
}
