using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using GSM.Application.Communications.Events;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Notifications.Push;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// N2: push web (registro de dispositivos, canal push del dispatcher con preferencias y horario sin avisos,
/// preferencias del staff) y el anuncio importante. Firebase no está configurado: el proveedor es un doble
/// (<see cref="CapturingPushSender"/>).
/// </summary>
public sealed class PushNotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    // ---------- 1 · dispositivos ----------

    [Fact]
    public async Task A_device_registers_once_and_unregisters_and_a_repeated_token_does_not_duplicate()
    {
        var world = await SeedAsync(1);
        var user = world.Members[0].UserId;
        var token = NewToken();

        PushDeviceResponse first;
        using (var scope = Member(world.GymId, user))
        {
            first = await scope.ServiceProvider.GetRequiredService<RegisterPushDeviceHandler>()
                .HandleAsync(new RegisterPushDeviceRequest(token, "web_member"), default);
        }

        using (var scope = Member(world.GymId, user))
        {
            var again = await scope.ServiceProvider.GetRequiredService<RegisterPushDeviceHandler>()
                .HandleAsync(new RegisterPushDeviceRequest(token, "web_member"), default);
            Assert.Equal(first.Id, again.Id);
        }

        Assert.Equal(1, await CountDevicesAsync(token));

        // Otra persona no puede dar de baja el dispositivo ajeno; la dueña sí, y repetirlo no falla.
        using (var scope = Member(world.GymId, Guid.CreateVersion7()))
        {
            await scope.ServiceProvider.GetRequiredService<UnregisterPushDeviceHandler>()
                .HandleAsync(new UnregisterPushDeviceRequest(token), default);
        }

        Assert.True(await IsActiveAsync(token));

        for (var i = 0; i < 2; i++)
        {
            using var scope = Member(world.GymId, user);
            await scope.ServiceProvider.GetRequiredService<UnregisterPushDeviceHandler>()
                .HandleAsync(new UnregisterPushDeviceRequest(token), default);
        }

        Assert.False(await IsActiveAsync(token));

        // Registrarlo de nuevo lo reactiva sin crear otra fila.
        using (var scope = Member(world.GymId, user))
        {
            await scope.ServiceProvider.GetRequiredService<RegisterPushDeviceHandler>()
                .HandleAsync(new RegisterPushDeviceRequest(token, "android"), default);
        }

        Assert.True(await IsActiveAsync(token));
        Assert.Equal(1, await CountDevicesAsync(token));
    }

    [Fact]
    public async Task The_platform_must_match_the_role()
    {
        var world = await SeedAsync(1);
        using var member = Member(world.GymId, world.Members[0].UserId);
        await Assert.ThrowsAsync<ValidationDomainException>(() => member.ServiceProvider
            .GetRequiredService<RegisterPushDeviceHandler>()
            .HandleAsync(new RegisterPushDeviceRequest(NewToken(), "web_panel"), default));

        using var staff = Staff(world.GymId);
        await Assert.ThrowsAsync<ValidationDomainException>(() => staff.ServiceProvider
            .GetRequiredService<RegisterPushDeviceHandler>()
            .HandleAsync(new RegisterPushDeviceRequest(NewToken(), "web_member"), default));
    }

    // ---------- 2 · canal push y preferencias ----------

    [Fact]
    public async Task A_notice_goes_out_as_push_when_the_category_is_on_and_not_when_it_is_off()
    {
        var world = await SeedAsync(1);
        var person = world.Members[0];
        var token = NewToken();
        await RegisterAsync(world.GymId, person.UserId, token, "web_member");
        var push = factory.Services.GetRequiredService<CapturingPushSender>();
        var noon = AtLocal(Today, 12);

        await NotifyAsync(world.GymId, person.Id, NotificationType.ClassCancelled, NotificationCategory.Classes, "Clase 1");
        await RunDispatcherAtAsync(noon, Today);

        var sent = Assert.Single(push.SentTo(token));
        Assert.Equal("Clase 1", sent.Title);
        Assert.Equal("/app/clases", sent.Link);
        Assert.Equal(world.GymId, sent.GymId);
        Assert.Equal(DeliveryStatus.Sent, (await PushDeliveriesAsync(person.Id)).Single().Status);

        // Con el push de "Mis clases" apagado, el aviso llega a la bandeja pero no sale push.
        await SetPushAsync(world.GymId, person.UserId, "Classes", push: false);
        await NotifyAsync(world.GymId, person.Id, NotificationType.ClassCancelled, NotificationCategory.Classes, "Clase 2");
        await RunDispatcherAtAsync(noon, Today);

        Assert.Single(push.SentTo(token));
        var deliveries = await PushDeliveriesAsync(person.Id);
        Assert.Equal(2, deliveries.Count);
        Assert.Contains(deliveries, d => d.Status == DeliveryStatus.Skipped);
        Assert.Equal(2, await CountNoticesAsync(person.Id, NotificationType.ClassCancelled));

        // Otra categoría sigue encendida.
        await NotifyAsync(world.GymId, person.Id, NotificationType.AchievementUnlocked, NotificationCategory.Progress, "Logro");
        await RunDispatcherAtAsync(noon, Today);
        Assert.Equal(2, push.SentTo(token).Count);
    }

    [Fact]
    public async Task A_token_the_provider_rejects_revokes_the_device_and_a_member_without_devices_gets_no_push()
    {
        var world = await SeedAsync(2);
        var withDevice = world.Members[0];
        var token = NewToken();
        await RegisterAsync(world.GymId, withDevice.UserId, token, "android");
        var push = factory.Services.GetRequiredService<CapturingPushSender>();
        push.InvalidTokens[token] = true;

        await NotifyAsync(world.GymId, withDevice.Id, NotificationType.ClassCancelled, NotificationCategory.Classes, "Clase");
        await NotifyAsync(world.GymId, world.Members[1].Id, NotificationType.ClassCancelled, NotificationCategory.Classes, "Clase");
        await RunDispatcherAtAsync(AtLocal(Today, 12), Today);

        Assert.Empty(push.SentTo(token));
        Assert.False(await IsActiveAsync(token));
        Assert.Equal(DeliveryStatus.Skipped, (await PushDeliveriesAsync(withDevice.Id)).Single().Status);
        Assert.Empty(await PushDeliveriesAsync(world.Members[1].Id));
        Assert.Equal(2, await CountByStatusAsync(world.GymId, NotificationType.ClassCancelled, NotificationStatus.Sent));
    }

    // ---------- 3 · horario sin avisos ----------

    [Fact]
    public async Task News_and_promotions_wait_for_the_quiet_hours_to_end_but_classes_and_progress_go_out_at_night()
    {
        var world = await SeedAsync(1);
        var person = world.Members[0];
        var token = NewToken();
        await RegisterAsync(world.GymId, person.UserId, token, "web_member");
        await SetPushAsync(world.GymId, person.UserId, "Promotions", push: true);
        var push = factory.Services.GetRequiredService<CapturingPushSender>();
        var night = AtLocal(Today, 22);

        await PublishEventAtAsync(world.GymId, night, Announcement(world, "Noticia", AnnouncementCategory.GymNews));
        await PublishEventAtAsync(world.GymId, night, Announcement(world, "Oferta", AnnouncementCategory.Promotions));
        await NotifyAsync(world.GymId, person.Id, NotificationType.ClassCancelled, NotificationCategory.Classes, "Clase");
        await NotifyAsync(world.GymId, person.Id, NotificationType.AchievementUnlocked, NotificationCategory.Progress, "Logro");

        await RunDispatcherAtAsync(night, Today);

        Assert.Equal(["Clase", "Logro"], push.SentTo(token).Select(m => m.Title).Order().ToArray());
        Assert.Equal(1, await CountByStatusAsync(world.GymId, NotificationType.Announcement, NotificationStatus.Held, "Noticia"));
        Assert.Equal(1, await CountByStatusAsync(world.GymId, NotificationType.Announcement, NotificationStatus.Held, "Oferta"));

        // 6:59 todavía retenidos; 7:00 salen.
        await RunDispatcherAtAsync(AtLocal(Today.AddDays(1), 6, 59), Today.AddDays(1));
        Assert.Equal(2, push.SentTo(token).Count);

        await RunDispatcherAtAsync(AtLocal(Today.AddDays(1), 7), Today.AddDays(1));
        Assert.Equal(["Clase", "Logro", "Noticia", "Oferta"], push.SentTo(token).Select(m => m.Title).Order().ToArray());
        Assert.Equal(2, await CountByStatusAsync(world.GymId, NotificationType.Announcement, NotificationStatus.Sent));
    }

    // ---------- 4 · anuncio importante ----------

    [Fact]
    public async Task Only_the_owner_can_mark_an_announcement_as_important_and_it_records_who()
    {
        var world = await SeedAsync(1);

        using (var scope = Staff(world.GymId))
        {
            await Assert.ThrowsAsync<ForbiddenDomainException>(() => scope.ServiceProvider
                .GetRequiredService<CreateAnnouncementHandler>().HandleAsync(
                    new CreateAnnouncementRequest("Aviso grave", "Texto", false, null, false, Important: true), default));
        }

        // Un staff tampoco puede marcar (ni desmarcar) un borrador existente.
        Guid ownerId = Guid.CreateVersion7();
        Guid draftId;
        using (var scope = Scope(world.GymId, ownerId, UserRole.Owner))
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateAnnouncementHandler>().HandleAsync(
                new CreateAnnouncementRequest("Aviso grave", "Texto", false, null, false, Important: true), default);
            Assert.True(created.Important);
            Assert.Equal(ownerId, created.ImportantMarkedBy);
            draftId = created.Id;
        }

        using (var scope = Staff(world.GymId))
        {
            await Assert.ThrowsAsync<ForbiddenDomainException>(() => scope.ServiceProvider
                .GetRequiredService<UpdateAnnouncementHandler>().HandleAsync(
                    draftId, new UpdateAnnouncementRequest("Aviso grave", "Texto", null, false, Important: false), default));
            // Editar el texto sin tocar la marca sí está permitido.
            var edited = await scope.ServiceProvider.GetRequiredService<UpdateAnnouncementHandler>().HandleAsync(
                draftId, new UpdateAnnouncementRequest("Aviso grave 2", "Texto", null, false), default);
            Assert.True(edited.Important);
        }

        // Ya publicado no cambia de importante.
        using (var scope = Scope(world.GymId, ownerId, UserRole.Owner))
        {
            await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(draftId, default);
        }

        using (var scope = Scope(world.GymId, ownerId, UserRole.Owner))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<UpdateAnnouncementHandler>().HandleAsync(
                    draftId, new UpdateAnnouncementRequest("Aviso grave 2", "Texto", null, false, Important: false), default));
        }
    }

    [Fact]
    public async Task A_promotion_can_never_be_important()
    {
        var world = await SeedAsync(1);
        using var scope = Scope(world.GymId, Guid.CreateVersion7(), UserRole.Owner);
        var create = scope.ServiceProvider.GetRequiredService<CreateAnnouncementHandler>();
        var update = scope.ServiceProvider.GetRequiredService<UpdateAnnouncementHandler>();

        // Crear una promoción importante: 422.
        await Assert.ThrowsAsync<ValidationDomainException>(() => create.HandleAsync(
            new CreateAnnouncementRequest("2x1", "Promo", false, null, false, AnnouncementCategory.Promotions, Important: true), default));

        // Pasar a promoción una novedad importante: 422; marcar importante una promoción: 422.
        var news = await create.HandleAsync(
            new CreateAnnouncementRequest("Cierre", "Sin agua", false, null, false, AnnouncementCategory.GymNews, Important: true), default);
        await Assert.ThrowsAsync<ValidationDomainException>(() => update.HandleAsync(
            news.Id, new UpdateAnnouncementRequest("Cierre", "Sin agua", null, false, AnnouncementCategory.Promotions), default));
        var promo = await create.HandleAsync(
            new CreateAnnouncementRequest("3x2", "Promo", false, null, false, AnnouncementCategory.Promotions), default);
        await Assert.ThrowsAsync<ValidationDomainException>(() => update.HandleAsync(
            promo.Id, new UpdateAnnouncementRequest("3x2", "Promo", null, false, Important: true), default));

        // En el mismo cambio: desmarcar y pasar a promoción vale; pasar a novedad y marcar, también.
        var unmarked = await update.HandleAsync(
            news.Id, new UpdateAnnouncementRequest("Cierre", "Sin agua", null, false, AnnouncementCategory.Promotions, Important: false), default);
        Assert.False(unmarked.Important);
        var marked = await update.HandleAsync(
            promo.Id, new UpdateAnnouncementRequest("3x2", "Ahora es novedad", null, false, AnnouncementCategory.GymNews, Important: true), default);
        Assert.True(marked.Important);
    }

    [Fact]
    public async Task An_important_announcement_ignores_preferences_and_quiet_hours_stays_first_in_the_feed_and_skips_the_inbox()
    {
        var world = await SeedAsync(1);
        var person = world.Members[0];
        var token = NewToken();
        await RegisterAsync(world.GymId, person.UserId, token, "web_member");
        // El miembro apagó Novedades en todos los canales.
        await SetPushAsync(world.GymId, person.UserId, "News", push: false);

        var push = factory.Services.GetRequiredService<CapturingPushSender>();
        var night = AtLocal(Today, 22);
        var expires = Today.AddDays(2);

        // Uno normal fijado (publicado antes) y uno importante (después, por el Owner, de noche).
        var pinned = await CreateAsync(world.GymId, UserRole.Staff, "Fijado", pinned: true, important: false, expiresOn: null);
        await PublishAsync(world.GymId, UserRole.Staff, pinned, night.AddMinutes(-10), Today);
        var important = await CreateAsync(world.GymId, UserRole.Owner, "Cerrado por emergencia", pinned: false, important: true, expiresOn: expires);
        await PublishAsync(world.GymId, UserRole.Owner, important, night, Today);

        await RunDispatcherAtAsync(night, Today);

        // Salió ya, de noche y con Novedades apagado; el normal quedó retenido sin push.
        var message = Assert.Single(push.SentTo(token));
        Assert.Equal("Cerrado por emergencia", message.Title);
        Assert.Equal(1, await CountByStatusAsync(world.GymId, NotificationType.ImportantAnnouncement, NotificationStatus.Sent));

        // No entra en la bandeja del miembro (ni el importante ni el normal).
        using (var scope = Member(world.GymId, person.UserId))
        {
            var inbox = await scope.ServiceProvider.GetRequiredService<ListMyNotificationsHandler>().HandleAsync(1, 20, default);
            Assert.Empty(inbox.Items);
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<CountMyUnreadNotificationsHandler>().HandleAsync(default));
        }

        // Primero en el feed, antes que el fijado, hasta su fecha.
        using (var scope = Member(world.GymId, person.UserId))
        {
            var feed = await scope.ServiceProvider.GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 20, default);
            Assert.Equal(important, feed.Items[0].Id);
            Assert.True(feed.Items[0].Important);
            Assert.Equal(pinned, feed.Items[1].Id);
            Assert.False(feed.Items[1].Important);
        }

        using (var scope = Member(world.GymId, person.UserId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(expires);
            var feed = await scope.ServiceProvider.GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 20, default);
            Assert.Contains(feed.Items, i => i.Id == important);
        }

        using (var scope = Member(world.GymId, person.UserId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(expires.AddDays(1));
            var feed = await scope.ServiceProvider.GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 20, default);
            Assert.DoesNotContain(feed.Items, i => i.Id == important);
        }
    }

    // ---------- 5 · sin Firebase ----------

    [Fact]
    public async Task Without_firebase_the_dispatcher_does_not_fail_and_the_push_is_skipped()
    {
        using var noFirebase = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPushSender>();
            services.AddSingleton<IPushSender, LoggingPushSender>();
        }));

        var world = await SeedAsync(1);
        var person = world.Members[0];
        await RegisterAsync(world.GymId, person.UserId, NewToken(), "web_member");
        await NotifyAsync(world.GymId, person.Id, NotificationType.ClassCancelled, NotificationCategory.Classes, "Clase");

        using (var scope = noFirebase.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(AtLocal(Today, 12), Today);
            var result = await scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>().RunOnceAsync();
            Assert.True(result.LockAcquired);
            Assert.Equal(0, result.Failed);

            Assert.False(scope.ServiceProvider.GetRequiredService<IPushSender>().IsConfigured);
            Assert.False(scope.ServiceProvider.GetRequiredService<IPushWebConfigProvider>().Get().Enabled);
        }

        var delivery = Assert.Single(await PushDeliveriesAsync(person.Id));
        Assert.Equal(DeliveryStatus.Skipped, delivery.Status);
        Assert.Equal(1, await CountByStatusAsync(world.GymId, NotificationType.ClassCancelled, NotificationStatus.Sent));
    }

    [Fact]
    public void The_fcm_options_without_credentials_are_not_configured()
    {
        Assert.False(new FcmOptions().IsConfigured);
        Assert.False(new FcmOptions { ProjectId = "gsm-dev" }.IsConfigured);
        Assert.True(new FcmOptions { ProjectId = "gsm-dev", ServiceAccountJson = "{}" }.IsConfigured);
    }

    // ---------- 6 · staff ----------

    [Fact]
    public async Task Staff_push_preferences_default_off_for_rejected_entries_and_the_action_notice_reaches_only_who_asked()
    {
        var world = await SeedAsync(0);
        var asking = Guid.CreateVersion7();
        var silent = Guid.CreateVersion7();
        var askingToken = NewToken();
        var silentToken = NewToken();
        await RegisterStaffAsync(world.GymId, asking, askingToken);
        await RegisterStaffAsync(world.GymId, silent, silentToken);

        using (var scope = Scope(world.GymId, asking, UserRole.Staff))
        {
            var defaults = await scope.ServiceProvider.GetRequiredService<GetMyStaffPushPreferencesHandler>().HandleAsync(default);
            var item = Assert.Single(defaults.Items);
            Assert.Equal("CheckinRejected", item.Type);
            Assert.False(item.Push);

            var updated = await scope.ServiceProvider.GetRequiredService<UpdateMyStaffPushPreferencesHandler>().HandleAsync(
                new UpdateStaffPushPreferencesRequest([new StaffPushPreferenceUpdate("CheckinRejected", true)]), default);
            Assert.True(updated.Items[0].Push);
        }

        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            db.Add(Notification.CreateGymAction(
                world.GymId, NotificationType.CheckinRejected, Guid.CreateVersion7(), "Entrada rechazada", "x", null, AtLocal(Today, 12)));
            await db.SaveChangesAsync();
        }

        var push = factory.Services.GetRequiredService<CapturingPushSender>();
        await RunDispatcherAtAsync(AtLocal(Today, 12), Today);

        Assert.Single(push.SentTo(askingToken));
        Assert.Empty(push.SentTo(silentToken));
    }

    // ---------- helpers ----------

    private static string NewToken() => $"tok-{Guid.NewGuid():N}";

    private static AnnouncementPublished Announcement(World world, string title, AnnouncementCategory category)
        => new(Guid.CreateVersion7(), world.GymId, title, "Texto.", false,
            world.Members.Select(m => m.Id).ToList(), category);

    private static DateTimeOffset AtLocal(DateOnly day, int hour, int minute = 0)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(hour).AddMinutes(minute);

    private static DateOnly DayOf(DateTimeOffset instant)
        => DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    private async Task RunDispatcherAtAsync(DateTimeOffset instant, DateOnly day)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(instant, day);
        await scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>().RunOnceAsync();
    }

    private async Task PublishEventAtAsync<TEvent>(Guid gymId, DateTimeOffset instant, TEvent evt) where TEvent : IDomainEvent
    {
        using var scope = Staff(gymId);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(instant, DayOf(instant));
        await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().PublishAsync(evt, default);
    }

    private async Task NotifyAsync(
        Guid gymId, Guid memberId, NotificationType type, NotificationCategory category, string title)
    {
        using var scope = System();
        await scope.ServiceProvider.GetRequiredService<MemberNoticeWriter>().NotifyAsync(
            new MemberNotice(gymId, memberId, type, category, Guid.CreateVersion7(), title, "Detalle.", LinkFor(category)),
            sendEmail: false, default);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static string LinkFor(NotificationCategory category) => category switch
    {
        NotificationCategory.Classes => "/app/clases",
        NotificationCategory.Progress => "/app/logros",
        _ => "/app",
    };

    private async Task<Guid> CreateAsync(
        Guid gymId, UserRole role, string title, bool pinned, bool important, DateOnly? expiresOn)
    {
        using var scope = Scope(gymId, Guid.CreateVersion7(), role);
        return (await scope.ServiceProvider.GetRequiredService<CreateAnnouncementHandler>().HandleAsync(
            new CreateAnnouncementRequest(title, $"Texto de {title}.", pinned, expiresOn, false, Important: important), default)).Id;
    }

    private async Task PublishAsync(Guid gymId, UserRole role, Guid id, DateTimeOffset instant, DateOnly day)
    {
        using var scope = Scope(gymId, Guid.CreateVersion7(), role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(instant, day);
        await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
    }

    private async Task RegisterAsync(Guid gymId, Guid userId, string token, string platform)
    {
        using var scope = Member(gymId, userId);
        await scope.ServiceProvider.GetRequiredService<RegisterPushDeviceHandler>()
            .HandleAsync(new RegisterPushDeviceRequest(token, platform), default);
    }

    private async Task RegisterStaffAsync(Guid gymId, Guid userId, string token)
    {
        using var scope = Scope(gymId, userId, UserRole.Staff);
        await scope.ServiceProvider.GetRequiredService<RegisterPushDeviceHandler>()
            .HandleAsync(new RegisterPushDeviceRequest(token, "web_panel"), default);
    }

    private async Task SetPushAsync(Guid gymId, Guid userId, string category, bool push)
    {
        using var scope = Member(gymId, userId);
        await scope.ServiceProvider.GetRequiredService<UpdateMyNotificationPreferencesHandler>().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdate(category, push, push)]), default);
    }

    private async Task<int> CountDevicesAsync(string token)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<PushDevice>()
            .CountAsync(d => d.Token == token);
    }

    private async Task<bool> IsActiveAsync(string token)
    {
        using var scope = System();
        return (await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<PushDevice>()
            .SingleAsync(d => d.Token == token)).IsActive;
    }

    private async Task<IReadOnlyList<NotificationDelivery>> PushDeliveriesAsync(Guid memberId)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await (
            from d in db.Set<NotificationDelivery>().AsNoTracking()
            join n in db.Set<Notification>() on d.NotificationId equals n.Id
            where n.RecipientMemberId == memberId && d.Channel == DeliveryChannel.Push
            select d).ToListAsync();
    }

    private async Task<int> CountNoticesAsync(Guid memberId, NotificationType type)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .CountAsync(n => n.RecipientMemberId == memberId && n.Type == type);
    }

    private async Task<int> CountByStatusAsync(
        Guid gymId, NotificationType type, NotificationStatus status, string? title = null)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .CountAsync(n => n.GymId == gymId && n.Type == type && n.Status == status
                && (title == null || n.Title == title));
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Staff(Guid gymId) => Scope(gymId, Guid.CreateVersion7(), UserRole.Staff);

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
        var gym = Gym.Create("Push Gym", $"pu-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));

        var people = new List<Person>();
        for (var i = 0; i < members; i++)
        {
            var userId = Guid.CreateVersion7();
            var member = GSM.Domain.Members.Member.Create(
                gym.Id, "Miembro", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-60),
                email: $"m-{Guid.NewGuid():N}@test.local");
            member.LinkUser(userId);
            db.Add(member);
            people.Add(new Person(member.Id, userId));
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, people);
    }

    private sealed record Person(Guid Id, Guid UserId);

    private sealed record World(Guid GymId, IReadOnlyList<Person> Members);
}
