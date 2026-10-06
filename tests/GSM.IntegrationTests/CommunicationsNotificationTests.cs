using System.Net;
using GSM.Application.Achievements.Events;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Events;
using GSM.Application.Communications;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Achievements;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>Etapas 13.3 y 13.4: avisos por evento, cola de correos, preferencias y tarea periódica.</summary>
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

        Assert.Equal(0, await CountOutboxAsync(world.GymId));
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
            Assert.Equal(3, (await scope.ServiceProvider.GetRequiredService<GetEmailAudienceHandler>().HandleAsync(default)).Recipients);
            var publish = scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>();
            var published = await publish.HandleAsync(withEmail, default);
            Assert.Equal(new EmailDelivery(3, 0, 0), published.Delivery);
        }

        using (var scope = Staff(world.GymId))
        {
            await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(withoutEmail, default);
        }

        Assert.Equal(3, await CountOutboxAsync(world.GymId));
    }

    [Fact]
    public async Task The_job_sends_pending_emails_once_and_retries_failures_up_to_three_times()
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
        await RunJobAsync();
        var mine = sender.Sent.Where(m => world.Members.Any(x => x.Email == m.To)).ToList();
        Assert.Equal(2, mine.Count);
        Assert.Contains("/api/public/unsubscribe?t=", mine[0].HtmlBody);

        sender.Clear();
        await RunJobAsync();
        Assert.DoesNotContain(sender.Sent, m => world.Members.Any(x => x.Email == m.To));
        Assert.Equal(2, await CountOutboxAsync(world.GymId, EmailStatus.Sent));

        // Un correo que falla siempre queda Failed al tercer intento.
        Guid failingId;
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var item = EmailOutboxItem.Create(world.GymId, world.Members[0].Id, "falla@test.local", "x", "x", "x",
                EmailKind.ClassCancelled, Guid.CreateVersion7());
            db.Add(item);
            await db.SaveChangesAsync();
            failingId = item.Id;
        }

        for (var i = 0; i < 3; i++)
        {
            await RunJobAsync(failFor: "falla@test.local");
        }

        using (var scope = System())
        {
            var item = await scope.ServiceProvider.GetRequiredService<GsmDbContext>()
                .Set<EmailOutboxItem>().SingleAsync(e => e.Id == failingId);
            Assert.Equal(EmailStatus.Failed, item.Status);
            Assert.Equal(3, item.Attempts);
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
        Assert.Equal(1, await CountOutboxAsync(world.GymId, kind: EmailKind.MembershipExpiring));

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
            url = scope.ServiceProvider.GetRequiredService<IUnsubscribeLinks>().BuildUrl(member.Id, world.GymId);
        }

        var token = Uri.UnescapeDataString(url[(url.IndexOf("t=", StringComparison.Ordinal) + 2)..]);
        var client = factory.CreateClient();

        var tampered = await client.GetAsync($"/api/public/unsubscribe?t={Uri.EscapeDataString(token[..^2] + "xx")}");
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);

        var ok = await client.GetAsync($"/api/public/unsubscribe?t={Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var check = Member(world.GymId, member.UserId);
        Assert.True((await check.ServiceProvider.GetRequiredService<GetCommunicationPreferencesHandler>().HandleAsync(default)).EmailOptOut);
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

        // Purga: 91 días se borra, 89 se conserva.
        var world = await SeedAsync(1);
        Guid oldId, recentId;
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var old = MemberNotification.Create(world.GymId, world.Members[0].Id, NotificationType.AchievementUnlocked, Guid.CreateVersion7(), "Viejo", "", null);
            var recent = MemberNotification.Create(world.GymId, world.Members[0].Id, NotificationType.AchievementUnlocked, Guid.CreateVersion7(), "Reciente", "", null);
            db.AddRange(old, recent);
            await db.SaveChangesAsync();
            oldId = old.Id;
            recentId = recent.Id;
            var now = DateTimeOffset.UtcNow;
            await db.Set<MemberNotification>().Where(n => n.Id == oldId).ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, now.AddDays(-91)));
            await db.Set<MemberNotification>().Where(n => n.Id == recentId).ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, now.AddDays(-89)));
        }

        using (var scope = System())
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();
            await scope.ServiceProvider.GetRequiredService<RunCommunicationsJobHandler>().HandleAsync(50, default);
        }

        using (var scope = System())
        {
            var ids = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberNotification>()
                .Where(n => n.MemberId == world.Members[0].Id).Select(n => n.Id).ToListAsync();
            Assert.DoesNotContain(oldId, ids);
            Assert.Contains(recentId, ids);
        }
    }

    // ---------- helpers ----------

    private async Task RunJobAsync(string? failFor = null)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        var sender = factory.Services.GetRequiredService<CapturingEmailSender>();
        if (failFor is null)
        {
            await scope.ServiceProvider.GetRequiredService<RunCommunicationsJobHandler>().HandleAsync(500, default);
            return;
        }

        // Un remitente que falla solo para una dirección; el resto se comporta como el de captura.
        var handler = ActivatorUtilities.CreateInstance<RunCommunicationsJobHandler>(
            scope.ServiceProvider, new FailingFor(failFor, sender));
        await handler.HandleAsync(500, default);
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
        await scope.ServiceProvider.GetRequiredService<UpdateCommunicationPreferencesHandler>()
            .HandleAsync(new UpdateCommunicationPreferencesRequest(true), default);
    }

    private async Task<int> CountNotificationsAsync(Guid gymId, NotificationType type)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MemberNotification>()
            .CountAsync(n => n.GymId == gymId && n.Type == type);
    }

    private async Task<int> CountOutboxAsync(Guid gymId, EmailStatus? status = null, EmailKind? kind = null)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<EmailOutboxItem>()
            .CountAsync(e => e.GymId == gymId && (status == null || e.Status == status) && (kind == null || e.Kind == kind));
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
                gym.Id, "Socio", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-60), email: address);
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
