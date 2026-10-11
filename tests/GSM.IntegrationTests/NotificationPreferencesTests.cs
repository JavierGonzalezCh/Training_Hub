using System.Net;
using System.Security.Cryptography;
using System.Text;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Events;
using GSM.Application.Communications.Events;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// N4 + CO1: preferencias del miembro por categoría y canal, enlace de baja por categoría y horario sin
/// avisos del gym. La migración del <c>EmailOptOut</c> se probó con datos reales contra una base de
/// desarrollo (ver el resumen de la tarea): el esquema de las pruebas ya nace sin la tabla vieja.
/// </summary>
public sealed class NotificationPreferencesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    // ---------- 1 · valores por defecto y cambios ----------

    [Fact]
    public async Task Without_rows_the_defaults_apply_and_promotions_start_off_until_the_member_changes_them()
    {
        var world = await SeedAsync(1);
        var member = world.Members[0];

        using (var scope = Member(world.GymId, member.UserId))
        {
            var items = (await scope.ServiceProvider.GetRequiredService<GetMyNotificationPreferencesHandler>()
                .HandleAsync(default)).Items;

            Assert.Equal(["Membership", "Classes", "Progress", "News", "Promotions"], items.Select(i => i.Category));
            Assert.All(items.Where(i => i.Category != "Promotions"), i => Assert.True(i.Push && i.Email));
            var promotions = items.Single(i => i.Category == "Promotions");
            Assert.False(promotions.Push);
            Assert.False(promotions.Email);
        }

        using (var scope = Member(world.GymId, member.UserId))
        {
            await scope.ServiceProvider.GetRequiredService<UpdateMyNotificationPreferencesHandler>().HandleAsync(
                new UpdateNotificationPreferencesRequest(
                [
                    new NotificationPreferenceUpdate("Promotions", true, true),
                    new NotificationPreferenceUpdate("Classes", false, true),
                ]), default);
        }

        using (var scope = Member(world.GymId, member.UserId))
        {
            var items = (await scope.ServiceProvider.GetRequiredService<GetMyNotificationPreferencesHandler>()
                .HandleAsync(default)).Items;
            Assert.True(items.Single(i => i.Category == "Promotions") is { Push: true, Email: true });
            Assert.True(items.Single(i => i.Category == "Classes") is { Push: false, Email: true });
            Assert.True(items.Single(i => i.Category == "News") is { Push: true, Email: true }); // sin tocar
        }

        // Los avisos de seguridad no tienen preferencia: no se pueden configurar.
        using var failing = Member(world.GymId, member.UserId);
        await Assert.ThrowsAsync<ValidationDomainException>(() => failing.ServiceProvider
            .GetRequiredService<UpdateMyNotificationPreferencesHandler>().HandleAsync(
                new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdate("Security", false, false)]), default));
    }

    // ---------- 3 · apagar el correo de una categoría ----------

    [Fact]
    public async Task Turning_a_category_email_off_keeps_the_notice_but_skips_its_email_and_security_still_goes_out()
    {
        var world = await SeedAsync(2);
        var muted = world.Members[0];
        var other = world.Members[1];
        await SetPreferenceAsync(world.GymId, muted.UserId, "News", push: true, email: false);

        await PublishAtAsync(world.GymId, AtLocal(Today, 12), Announcement(world, sendEmail: true));

        Assert.Equal(2, await CountNoticesAsync(world.GymId, NotificationType.Announcement));
        Assert.Equal(DeliveryStatus.Skipped, await DeliveryStatusOfAsync(muted.Id));
        Assert.Equal(DeliveryStatus.Pending, await DeliveryStatusOfAsync(other.Id));

        var sender = factory.Services.GetRequiredService<CapturingEmailSender>();
        sender.Clear();
        await RunDispatcherAtAsync(AtLocal(Today, 12), Today);
        Assert.Null(sender.LastTo(muted.Email!));
        var mail = sender.LastTo(other.Email!);
        Assert.NotNull(mail);
        Assert.Contains("/api/public/unsubscribe?t=", mail.HtmlBody);

        // Con todos los correos apagados, un aviso de seguridad sale igual y sin enlace de baja.
        foreach (var category in new[] { "Membership", "Classes", "Progress", "News", "Promotions" })
        {
            await SetPreferenceAsync(world.GymId, muted.UserId, category, push: false, email: false);
        }

        using (var scope = System())
        {
            await scope.ServiceProvider.GetRequiredService<MemberNoticeWriter>().NotifyAsync(
                new MemberNotice(
                    world.GymId, muted.Id, NotificationType.ClassCancelled, NotificationCategory.Security,
                    Guid.CreateVersion7(), "Entrada con tu documento", "Se registró tu entrada.", null),
                sendEmail: true, default);
            await scope.ServiceProvider.GetRequiredService<GSM.Application.Common.Abstractions.IUnitOfWork>().SaveChangesAsync();
        }

        sender.Clear();
        await RunDispatcherAtAsync(AtLocal(Today, 12), Today);
        var security = sender.LastTo(muted.Email!);
        Assert.NotNull(security);
        Assert.DoesNotContain("unsubscribe", security.HtmlBody);
    }

    // ---------- 4 · enlace de baja por categoría ----------

    [Fact]
    public async Task The_unsubscribe_link_turns_off_only_the_email_of_its_category_and_old_links_turn_off_all()
    {
        var world = await SeedAsync(2);
        var client = factory.CreateClient();

        string Url(Guid memberId, NotificationCategory category)
        {
            using var scope = System();
            return scope.ServiceProvider.GetRequiredService<IUnsubscribeLinks>().BuildUrl(memberId, world.GymId, category);
        }

        var promotions = Url(world.Members[0].Id, NotificationCategory.Promotions);
        var classes = Url(world.Members[0].Id, NotificationCategory.Classes);
        var ok = await client.GetAsync(new Uri(classes));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Mis clases", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri(classes))).StatusCode); // idempotente

        using (var scope = Member(world.GymId, world.Members[0].UserId))
        {
            var items = (await scope.ServiceProvider.GetRequiredService<GetMyNotificationPreferencesHandler>()
                .HandleAsync(default)).Items;
            Assert.False(items.Single(i => i.Category == "Classes").Email);
            Assert.True(items.Single(i => i.Category == "Classes").Push);
            Assert.True(items.Single(i => i.Category == "News").Email);
            Assert.True(items.Single(i => i.Category == "Membership").Email);
        }

        // Un enlace con la categoría alterada ya no coincide con su firma.
        var tampered = promotions.Replace(".promotions.", ".news.");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(new Uri(tampered))).StatusCode);

        // Un enlace anterior a N4 (sin categoría) apaga el correo de todas.
        var legacyPayload = $"{world.Members[1].Id:N}.{world.GymId:N}";
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("integration-tests-unsubscribe-key-32-bytes!!"), Encoding.UTF8.GetBytes($"unsubscribe|{legacyPayload}"));
        var signature = Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var legacy = await client.GetAsync($"/api/public/unsubscribe?t={Uri.EscapeDataString($"{legacyPayload}.{signature}")}");
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);

        using (var scope = Member(world.GymId, world.Members[1].UserId))
        {
            var items = (await scope.ServiceProvider.GetRequiredService<GetMyNotificationPreferencesHandler>()
                .HandleAsync(default)).Items;
            Assert.All(items, i => Assert.False(i.Email));
        }
    }

    // ---------- 5 · horario sin avisos ----------

    [Fact]
    public async Task A_news_announcement_at_22h_is_held_until_7h_while_exempt_notices_go_out_at_once()
    {
        var world = await SeedAsync(2);
        var sender = factory.Services.GetRequiredService<CapturingEmailSender>();
        var ids = world.Members.Select(m => m.Id).ToList();
        var night = AtLocal(Today, 22);

        await PublishAtAsync(world.GymId, night, Announcement(world, sendEmail: true));
        var classId = await SeedClassAsync(world.GymId);
        await PublishAtAsync(world.GymId, night, new ClassSessionCancelled(classId, world.GymId, Today.AddDays(2), ids));

        sender.Clear();
        await RunDispatcherAtAsync(night, Today);

        // El anuncio queda retenido (con su aviso ya en el sistema); la clase cancelada sale en el acto.
        Assert.Equal(2, await CountByStatusAsync(world.GymId, NotificationType.Announcement, NotificationStatus.Held));
        Assert.Equal(2, await CountByStatusAsync(world.GymId, NotificationType.ClassCancelled, NotificationStatus.Sent));
        Assert.Equal(2, sender.Sent.Count(m => world.Members.Any(x => x.Email == m.To)));
        Assert.All(sender.Sent.Where(m => world.Members.Any(x => x.Email == m.To)), m => Assert.Contains("se canceló", m.Subject));
        Assert.Equal(AtLocal(Today.AddDays(1), 7), await ReleaseAtAsync(world.GymId));

        // 6:59: todavía dentro del horario sin avisos.
        sender.Clear();
        await RunDispatcherAtAsync(AtLocal(Today.AddDays(1), 6, 59), Today.AddDays(1));
        Assert.DoesNotContain(sender.Sent, m => world.Members.Any(x => x.Email == m.To));
        Assert.Equal(2, await CountByStatusAsync(world.GymId, NotificationType.Announcement, NotificationStatus.Held));

        // 7:00: el dispatcher libera y envía.
        await RunDispatcherAtAsync(AtLocal(Today.AddDays(1), 7), Today.AddDays(1));
        Assert.Equal(2, sender.Sent.Count(m => world.Members.Any(x => x.Email == m.To) && m.Subject.Contains("Festivo")));
        Assert.Equal(2, await CountByStatusAsync(world.GymId, NotificationType.Announcement, NotificationStatus.Sent));
    }

    [Fact]
    public async Task Promotions_wait_for_the_quiet_hours_too_and_staff_notices_never_do()
    {
        var world = await SeedAsync(1);
        await SetPreferenceAsync(world.GymId, world.Members[0].UserId, "Promotions", push: true, email: true);
        var night = AtLocal(Today, 23);

        await PublishAtAsync(world.GymId, night, Announcement(world, sendEmail: true, AnnouncementCategory.Promotions));
        await RunDispatcherAtAsync(night, Today);
        Assert.Equal(1, await CountByStatusAsync(world.GymId, NotificationType.Announcement, NotificationStatus.Held));

        // Un aviso al staff (sin categoría de miembro) no se retiene en el horario sin avisos.
        Guid staffNoticeId;
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var notice = Notification.CreateGymAction(
                world.GymId, NotificationType.CheckinRejected, Guid.CreateVersion7(), "Entrada rechazada", "x", null, night);
            db.Add(notice);
            await db.SaveChangesAsync();
            staffNoticeId = notice.Id;
        }

        await RunDispatcherAtAsync(night, Today);
        using (var scope = System())
        {
            var notice = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
                .SingleAsync(n => n.Id == staffNoticeId);
            Assert.Equal(NotificationStatus.Sent, notice.Status);
        }
    }

    [Fact]
    public async Task The_owner_changes_or_disables_the_quiet_hours_and_an_empty_window_is_rejected()
    {
        var world = await SeedAsync(1);

        using (var scope = Owner(world.GymId))
        {
            var current = await scope.ServiceProvider.GetRequiredService<GetNotificationSettingsHandler>().HandleAsync(default);
            Assert.Equal(new NotificationSettingsResponse(true, new TimeOnly(21, 0), new TimeOnly(7, 0)), current);

            await Assert.ThrowsAsync<ValidationDomainException>(() => scope.ServiceProvider
                .GetRequiredService<UpdateNotificationSettingsHandler>().HandleAsync(
                    new UpdateNotificationSettingsRequest(true, new TimeOnly(8, 0), new TimeOnly(8, 0)), default));

            var updated = await scope.ServiceProvider.GetRequiredService<UpdateNotificationSettingsHandler>().HandleAsync(
                new UpdateNotificationSettingsRequest(true, new TimeOnly(23, 0), new TimeOnly(5, 0)), default);
            Assert.Equal(new TimeOnly(23, 0), updated.QuietStart);
        }

        // 22:00 ya queda fuera del horario 23:00-05:00: el anuncio sale de inmediato.
        var sender = factory.Services.GetRequiredService<CapturingEmailSender>();
        var night = AtLocal(Today, 22);
        await PublishAtAsync(world.GymId, night, Announcement(world, sendEmail: true));
        sender.Clear();
        await RunDispatcherAtAsync(night, Today);
        Assert.Equal(1, await CountByStatusAsync(world.GymId, NotificationType.Announcement, NotificationStatus.Sent));
        Assert.Single(sender.Sent, m => m.To == world.Members[0].Email);

        // Con el horario apagado, ni de madrugada se retiene.
        var other = await SeedAsync(1);
        using (var scope = Owner(other.GymId))
        {
            await scope.ServiceProvider.GetRequiredService<UpdateNotificationSettingsHandler>().HandleAsync(
                new UpdateNotificationSettingsRequest(false, new TimeOnly(21, 0), new TimeOnly(7, 0)), default);
        }

        var dawn = AtLocal(Today, 3);
        await PublishAtAsync(other.GymId, dawn, Announcement(other, sendEmail: false));
        await RunDispatcherAtAsync(dawn, Today);
        Assert.Equal(1, await CountByStatusAsync(other.GymId, NotificationType.Announcement, NotificationStatus.Sent));
    }

    // ---------- helpers ----------

    private static AnnouncementPublished Announcement(
        World world, bool sendEmail, AnnouncementCategory category = AnnouncementCategory.GymNews)
        => new(Guid.CreateVersion7(), world.GymId, "Festivo", "Cerramos el lunes.", sendEmail,
            world.Members.Select(m => m.Id).ToList(), category);

    private static DateTimeOffset AtLocal(DateOnly day, int hour, int minute = 0)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(hour).AddMinutes(minute);

    private async Task PublishAtAsync<TEvent>(Guid gymId, DateTimeOffset instant, TEvent evt) where TEvent : IDomainEvent
    {
        using var scope = Staff(gymId);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(instant, DayOf(instant));
        await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().PublishAsync(evt, default);
    }

    private async Task RunDispatcherAtAsync(DateTimeOffset instant, DateOnly day)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(instant, day);
        await scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>().RunOnceAsync();
    }

    private static DateOnly DayOf(DateTimeOffset instant)
        => DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    private async Task SetPreferenceAsync(Guid gymId, Guid userId, string category, bool push, bool email)
    {
        using var scope = Member(gymId, userId);
        await scope.ServiceProvider.GetRequiredService<UpdateMyNotificationPreferencesHandler>().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdate(category, push, email)]), default);
    }

    private async Task<int> CountNoticesAsync(Guid gymId, NotificationType type)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .CountAsync(n => n.GymId == gymId && n.Type == type);
    }

    private async Task<int> CountByStatusAsync(Guid gymId, NotificationType type, NotificationStatus status)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .CountAsync(n => n.GymId == gymId && n.Type == type && n.Status == status);
    }

    private async Task<DateTimeOffset?> ReleaseAtAsync(Guid gymId)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .Where(n => n.GymId == gymId && n.Status == NotificationStatus.Held)
            .Select(n => n.ReleaseAt)
            .FirstOrDefaultAsync();
    }

    private async Task<DeliveryStatus> DeliveryStatusOfAsync(Guid memberId)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await (
            from d in db.Set<NotificationDelivery>()
            join n in db.Set<Notification>() on d.NotificationId equals n.Id
            where n.RecipientMemberId == memberId
            select d.Status).SingleAsync();
    }

    private async Task<Guid> SeedClassAsync(Guid gymId)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gymClass = GymClass.Create(gymId, Today.AddDays(-30), new GymClassDraft(
            "Spinning", null, null, 10, new TimeOnly(7, 0), 60, true,
            [ClassDayOfWeek.Monday, ClassDayOfWeek.Tuesday, ClassDayOfWeek.Wednesday, ClassDayOfWeek.Thursday,
             ClassDayOfWeek.Friday, ClassDayOfWeek.Saturday, ClassDayOfWeek.Sunday], null));
        db.Add(gymClass);
        await db.SaveChangesAsync();
        return gymClass.Id;
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

    private async Task<World> SeedAsync(int members)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("Prefs Gym", $"pf-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));

        var people = new List<Person>();
        for (var i = 0; i < members; i++)
        {
            var userId = Guid.CreateVersion7();
            var address = $"m-{Guid.NewGuid():N}@test.local";
            var member = GSM.Domain.Members.Member.Create(
                gym.Id, "Miembro", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-60), email: address);
            member.LinkUser(userId);
            db.Add(member);
            people.Add(new Person(member.Id, userId, address));
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, people);
    }

    private sealed record Person(Guid Id, Guid UserId, string? Email);

    private sealed record World(Guid GymId, IReadOnlyList<Person> Members);
}
