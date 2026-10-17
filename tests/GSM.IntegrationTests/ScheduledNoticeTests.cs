using GSM.Application.Classes;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Events;
using GSM.Application.Communications.Events;
using GSM.Application.Notifications;
using GSM.Domain.Classes;
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
/// Notifications N5: avisos programados y cancelables. Recordatorio de clase (10 minutos antes) y su
/// cancelación, recordatorio al instructor, cumpleaños (el mecanismo que usará CO5) y recordatorios de
/// vencimiento a las 8:00. El tiempo va por <c>MutableClock</c>; el dispatcher se corre a mano.
/// </summary>
public sealed class ScheduledNoticeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly NextMonday = Monday.AddDays(7);

    // ---------- 1 · recordatorio de clase ----------

    [Fact]
    public async Task Booking_schedules_a_reminder_10_minutes_before_that_stays_hidden_until_its_time()
    {
        var w = await SeedAsync(1);

        await BookAsync(w, 0);

        var reminder = await SingleNoticeAsync(w.GymId, NotificationType.ClassReminder, memberId: w.MemberIds[0]);
        Assert.Equal(NotificationStatus.Pending, reminder.Status);
        Assert.Equal(NotificationCategory.Classes, reminder.Category);
        Assert.Equal(AtLocal(NextMonday, 6, 50), reminder.ScheduledFor);

        var instructor = await SingleNoticeAsync(w.GymId, NotificationType.InstructorClassReminder, userId: w.InstructorUserId);
        Assert.Equal(AtLocal(NextMonday, 6, 50), instructor.ScheduledFor);
        Assert.Equal(reminder.ReferenceId, instructor.ReferenceId);

        // Antes de su hora no está en la bandeja ni el dispatcher lo envía.
        Assert.Empty(await InboxAsync(w, 0, AtLocal(Monday, 13)));
        await RunDispatcherAtAsync(AtLocal(NextMonday, 6, 49), NextMonday, w.GymId);
        Assert.Equal(NotificationStatus.Pending, (await SingleNoticeAsync(w.GymId, NotificationType.ClassReminder, memberId: w.MemberIds[0])).Status);
        Assert.Empty(await InboxAsync(w, 0, AtLocal(NextMonday, 6, 49)));

        // A las 6:50 sale: queda enviado, aparece en la bandeja y no lleva correo (llegaría tarde).
        await RunDispatcherAtAsync(AtLocal(NextMonday, 6, 50), NextMonday, w.GymId);
        var sent = await SingleNoticeAsync(w.GymId, NotificationType.ClassReminder, memberId: w.MemberIds[0]);
        Assert.Equal(NotificationStatus.Sent, sent.Status);
        Assert.Equal(
            NotificationStatus.Sent,
            (await SingleNoticeAsync(w.GymId, NotificationType.InstructorClassReminder, userId: w.InstructorUserId)).Status);
        Assert.Equal(0, await EmailDeliveriesAsync(sent.Id));

        var inbox = await InboxAsync(w, 0, AtLocal(NextMonday, 6, 51));
        var item = Assert.Single(inbox);
        Assert.Contains("empieza en 10 minutos", item.Title);
    }

    [Fact]
    public async Task Cancelling_the_booking_cancels_the_reminder_and_booking_again_reactivates_the_same_one()
    {
        var w = await SeedAsync(1);
        await BookAsync(w, 0);

        using (var scope = AsMember(w, 0, AtLocal(Monday, 12)))
        {
            await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>().HandleAsync(w.ClassId, NextMonday, default);
        }

        var cancelled = await SingleNoticeAsync(w.GymId, NotificationType.ClassReminder, memberId: w.MemberIds[0]);
        Assert.Equal(NotificationStatus.Cancelled, cancelled.Status);

        await RunDispatcherAtAsync(AtLocal(NextMonday, 6, 51), NextMonday, w.GymId);
        Assert.Equal(
            NotificationStatus.Cancelled,
            (await SingleNoticeAsync(w.GymId, NotificationType.ClassReminder, memberId: w.MemberIds[0])).Status);
        Assert.Empty(await InboxAsync(w, 0, AtLocal(NextMonday, 6, 52)));

        // Reservar de nuevo reactiva el mismo aviso (no se crea otro) y esta vez sale.
        await BookAsync(w, 0);
        var again = await SingleNoticeAsync(w.GymId, NotificationType.ClassReminder, memberId: w.MemberIds[0]);
        Assert.Equal(cancelled.Id, again.Id);
        Assert.Equal(NotificationStatus.Pending, again.Status);
        Assert.Equal(AtLocal(NextMonday, 6, 50), again.ScheduledFor);

        await RunDispatcherAtAsync(AtLocal(NextMonday, 6, 51), NextMonday, w.GymId);
        Assert.Equal(
            NotificationStatus.Sent,
            (await SingleNoticeAsync(w.GymId, NotificationType.ClassReminder, memberId: w.MemberIds[0])).Status);
    }

    [Fact]
    public async Task Cancelling_the_session_cancels_every_reminder_including_the_instructors()
    {
        var w = await SeedAsync(2);
        await BookAsync(w, 0);
        await BookAsync(w, 1);

        using (var scope = AsOwner(w, AtLocal(Monday, 12)))
        {
            await scope.ServiceProvider.GetRequiredService<CancelSessionHandler>()
                .HandleAsync(w.ClassId, NextMonday, "Instructor enfermo", default);
        }

        Assert.Equal(2, await CountByStatusAsync(w.GymId, NotificationType.ClassReminder, NotificationStatus.Cancelled));
        Assert.Equal(1, await CountByStatusAsync(w.GymId, NotificationType.InstructorClassReminder, NotificationStatus.Cancelled));
        // El aviso de "clase cancelada" de siempre sigue creándose.
        Assert.Equal(2, await CountNoticesAsync(w.GymId, NotificationType.ClassCancelled));

        await RunDispatcherAtAsync(AtLocal(NextMonday, 6, 51), NextMonday, w.GymId);
        Assert.Equal(0, await CountByStatusAsync(w.GymId, NotificationType.ClassReminder, NotificationStatus.Sent));
        Assert.Equal(0, await CountByStatusAsync(w.GymId, NotificationType.InstructorClassReminder, NotificationStatus.Sent));
    }

    [Fact]
    public async Task A_booking_made_inside_the_last_10_minutes_gets_no_reminder_and_repeating_the_event_does_not_duplicate()
    {
        var w = await SeedAsync(1);
        var last10 = AtLocal(NextMonday, 6, 55);

        await PublishAtAsync(
            w.GymId, last10, new ClassBooked(w.GymId, w.MemberIds[0], w.ClassId, NextMonday, new TimeOnly(7, 0), w.InstructorUserId));
        Assert.Equal(0, await CountNoticesAsync(w.GymId, NotificationType.ClassReminder));
        Assert.Equal(0, await CountNoticesAsync(w.GymId, NotificationType.InstructorClassReminder));

        var early = AtLocal(Monday, 12);
        var evt = new ClassBooked(w.GymId, w.MemberIds[0], w.ClassId, NextMonday, new TimeOnly(7, 0), w.InstructorUserId);
        await PublishAtAsync(w.GymId, early, evt);
        await PublishAtAsync(w.GymId, early, evt);
        Assert.Equal(1, await CountNoticesAsync(w.GymId, NotificationType.ClassReminder));
        Assert.Equal(1, await CountNoticesAsync(w.GymId, NotificationType.InstructorClassReminder));

        // Cancelar dos veces tampoco falla ni cambia nada más.
        var cancel = new ClassBookingCancelled(w.GymId, w.MemberIds[0], w.ClassId, NextMonday);
        await PublishAtAsync(w.GymId, early, cancel);
        await PublishAtAsync(w.GymId, early, cancel);
        Assert.Equal(1, await CountByStatusAsync(w.GymId, NotificationType.ClassReminder, NotificationStatus.Cancelled));
    }

    // ---------- 2 · cumpleaños (el mecanismo que usará CO5) ----------

    [Fact]
    public async Task A_birthday_greeting_is_scheduled_for_its_hour_and_the_event_is_idempotent()
    {
        var w = await SeedAsync(1);
        var birthday = NextMonday;
        var sendAt = AtLocal(birthday, 8);
        var evt = new BirthdayGreetingDue(
            w.GymId, w.MemberIds[0], birthday, "¡Feliz cumpleaños, Miembro!", "Hoy tu entrenamiento va por cuenta de la casa.", sendAt);

        var eve = AtLocal(birthday.AddDays(-1), 22);
        await PublishAtAsync(w.GymId, eve, evt);
        await PublishAtAsync(w.GymId, eve, evt);

        var greeting = await SingleNoticeAsync(w.GymId, NotificationType.Birthday, memberId: w.MemberIds[0]);
        Assert.Equal(NotificationStatus.Pending, greeting.Status);
        Assert.Equal(NotificationCategory.News, greeting.Category);
        Assert.Equal(sendAt, greeting.ScheduledFor);

        await RunDispatcherAtAsync(AtLocal(birthday, 7, 59), birthday, w.GymId);
        Assert.Equal(NotificationStatus.Pending, (await SingleNoticeAsync(w.GymId, NotificationType.Birthday, memberId: w.MemberIds[0])).Status);
        Assert.Empty(await InboxAsync(w, 0, AtLocal(birthday, 7, 59)));

        await RunDispatcherAtAsync(sendAt, birthday, w.GymId);
        Assert.Equal(NotificationStatus.Sent, (await SingleNoticeAsync(w.GymId, NotificationType.Birthday, memberId: w.MemberIds[0])).Status);
        Assert.Single(await InboxAsync(w, 0, sendAt.AddMinutes(1)));
    }

    // ---------- 3 · vencimientos a las 8:00 (CO7 sigue generándolos) ----------

    [Fact]
    public async Task Membership_reminders_generated_before_8am_wait_for_8am_and_later_ones_go_out_at_once()
    {
        var w = await SeedAsync(1);
        var day = NextMonday;
        var planEnd = day.AddDays(2);

        var dawn = AtLocal(day, 0, 5);
        await PublishAtAsync(
            w.GymId, dawn,
            new MembershipReminderDue(w.GymId, w.MemberIds[0], Guid.CreateVersion7(), "Mensual", planEnd, 2, MembershipReminderKind.DaysBefore));
        var early = await SingleNoticeAsync(w.GymId, NotificationType.MembershipExpiring, memberId: w.MemberIds[0]);
        Assert.Equal(AtLocal(day, 8), early.ScheduledFor);

        await RunDispatcherAtAsync(AtLocal(day, 7, 59), day, w.GymId);
        Assert.Equal(NotificationStatus.Pending, (await SingleNoticeAsync(w.GymId, NotificationType.MembershipExpiring, memberId: w.MemberIds[0])).Status);
        await RunDispatcherAtAsync(AtLocal(day, 8), day, w.GymId);
        Assert.Equal(NotificationStatus.Sent, (await SingleNoticeAsync(w.GymId, NotificationType.MembershipExpiring, memberId: w.MemberIds[0])).Status);

        // Generado ya pasadas las 8:00: sale en el acto, como hasta ahora.
        var noon = AtLocal(day, 12);
        await PublishAtAsync(
            w.GymId, noon,
            new MembershipReminderDue(w.GymId, w.MemberIds[0], Guid.CreateVersion7(), "Mensual", day, 0, MembershipReminderKind.ExpiresToday));
        var expiresToday = await DbAsync(db => db.Set<Notification>()
            .Where(n => n.GymId == w.GymId && n.Type == NotificationType.MembershipExpiring && n.ScheduledFor == noon)
            .SingleAsync());
        Assert.Equal(noon, expiresToday.ScheduledFor);
    }

    [Fact]
    public async Task A_scheduled_expiry_reminder_is_cancelled_when_the_member_renews_or_the_membership_is_cancelled()
    {
        var w = await SeedAsync(1);
        var day = NextMonday;
        var dawn = AtLocal(day, 0, 5);
        var memberId = w.MemberIds[0];

        // Dos membresías con su recordatorio programado para las 8:00 (los de "vence hoy" usan otra referencia).
        var renewedMembership = Guid.CreateVersion7();
        var cancelledMembership = Guid.CreateVersion7();
        await PublishAtAsync(w.GymId, dawn,
            new MembershipReminderDue(w.GymId, memberId, renewedMembership, "Mensual", day, 0, MembershipReminderKind.ExpiresToday));
        await PublishAtAsync(w.GymId, dawn,
            new MembershipReminderDue(w.GymId, memberId, cancelledMembership, "Mensual", day.AddDays(2), 2, MembershipReminderKind.DaysBefore));
        Assert.Equal(2, await CountByStatusAsync(w.GymId, NotificationType.MembershipExpiring, NotificationStatus.Pending));

        // Renueva la primera: su recordatorio se cancela; el de la otra sigue.
        await PublishAtAsync(w.GymId, dawn,
            new GSM.Application.Memberships.Events.MembershipRenewed(Guid.CreateVersion7(), memberId, w.GymId, Guid.CreateVersion7(), renewedMembership));
        Assert.Equal(1, await CountByStatusAsync(w.GymId, NotificationType.MembershipExpiring, NotificationStatus.Cancelled));
        Assert.Equal(1, await CountByStatusAsync(w.GymId, NotificationType.MembershipExpiring, NotificationStatus.Pending));

        // Cancela la segunda: también se cancela. Repetir el evento no cambia nada.
        var cancelled = new GSM.Application.Memberships.Events.MembershipCancelled(cancelledMembership, memberId, w.GymId);
        await PublishAtAsync(w.GymId, dawn, cancelled);
        await PublishAtAsync(w.GymId, dawn, cancelled);
        Assert.Equal(2, await CountByStatusAsync(w.GymId, NotificationType.MembershipExpiring, NotificationStatus.Cancelled));

        // El dispatcher no envía nada de lo cancelado.
        await RunDispatcherAtAsync(AtLocal(day, 8), day, w.GymId);
        Assert.Equal(0, await CountByStatusAsync(w.GymId, NotificationType.MembershipExpiring, NotificationStatus.Sent));
        Assert.Empty(await InboxAsync(w, 0, AtLocal(day, 9)));
    }

    // ---------- siembra y ayudas ----------

    private async Task BookAsync(World w, int memberIndex)
    {
        using var scope = AsMember(w, memberIndex, AtLocal(Monday, 12));
        await scope.ServiceProvider.GetRequiredService<BookClassHandler>().HandleAsync(w.ClassId, NextMonday, default);
    }

    private async Task<IReadOnlyList<GSM.Application.Notifications.Contracts.NotificationItem>> InboxAsync(
        World w, int memberIndex, DateTimeOffset at)
    {
        using var scope = AsMember(w, memberIndex, at);
        return (await scope.ServiceProvider.GetRequiredService<ListMyNotificationsHandler>().HandleAsync(1, 20, default)).Items;
    }

    private async Task<Notification> SingleNoticeAsync(
        Guid gymId, NotificationType type, Guid? memberId = null, Guid? userId = null)
        => await DbAsync(db => db.Set<Notification>().AsNoTracking()
            .SingleAsync(n => n.GymId == gymId && n.Type == type
                && n.RecipientMemberId == memberId && n.RecipientUserId == userId));

    private Task<int> CountNoticesAsync(Guid gymId, NotificationType type)
        => DbAsync(db => db.Set<Notification>().CountAsync(n => n.GymId == gymId && n.Type == type));

    private Task<int> CountByStatusAsync(Guid gymId, NotificationType type, NotificationStatus status)
        => DbAsync(db => db.Set<Notification>().CountAsync(n => n.GymId == gymId && n.Type == type && n.Status == status));

    private Task<int> EmailDeliveriesAsync(Guid notificationId)
        => DbAsync(db => db.Set<NotificationDelivery>()
            .CountAsync(d => d.NotificationId == notificationId && d.Channel == DeliveryChannel.Email));

    private async Task<T> DbAsync<T>(Func<GsmDbContext, Task<T>> query)
    {
        using var scope = System();
        return await query(scope.ServiceProvider.GetRequiredService<GsmDbContext>());
    }

    private static DateTimeOffset AtLocal(DateOnly day, int hour, int minute = 0)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(hour).AddMinutes(minute);

    private static DateOnly DayOf(DateTimeOffset instant)
        => DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    private async Task PublishAtAsync<TEvent>(Guid gymId, DateTimeOffset instant, TEvent evt) where TEvent : IDomainEvent
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(instant, DayOf(instant));
        await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().PublishAsync(evt, default);
    }

    private async Task RunDispatcherAtAsync(DateTimeOffset instant, DateOnly day, Guid gymId)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(instant, day);
        await scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>().RunOnceAsync(onlyGymId: gymId);
    }

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

    private IServiceScope AsMember(World w, int index, DateTimeOffset at)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserIds[index], w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(at, DayOf(at));
        return scope;
    }

    private async Task<World> SeedAsync(int members)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("N5 Gym", $"n5-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));

        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.Add(plan);

        var memberIds = new List<Guid>();
        var memberUserIds = new List<Guid>();
        for (var i = 0; i < members; i++)
        {
            var userId = Guid.CreateVersion7();
            var member = Member.Create(
                gym.Id, "Miembro", $"{i}", $"doc-{Guid.NewGuid():N}"[..12], Monday.AddDays(-60),
                email: $"n5-{Guid.NewGuid():N}@test.local");
            member.LinkUser(userId);
            db.Add(member);
            db.Add(Membership.Create(gym.Id, member.Id, plan.Id, Monday.AddDays(-5), 120));
            memberIds.Add(member.Id);
            memberUserIds.Add(userId);
        }

        var instructorUserId = Guid.CreateVersion7();
        var gymClass = GymClass.Create(gym.Id, Monday.AddDays(-30), new GymClassDraft(
            "Spinning", null, "Ana", 10, new TimeOnly(7, 0), 45, true, [ClassDayOfWeek.Monday], null,
            InstructorUserId: instructorUserId));
        db.Add(gymClass);

        await db.SaveChangesAsync();
        return new World(gym.Id, gymClass.Id, instructorUserId, memberIds, memberUserIds);
    }

    private sealed record World(
        Guid GymId, Guid ClassId, Guid InstructorUserId, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> MemberUserIds);
}
