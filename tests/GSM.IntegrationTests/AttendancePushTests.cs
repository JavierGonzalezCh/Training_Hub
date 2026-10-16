using GSM.Application.Attendance.Events;
using GSM.Application.Common.Events;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Attendance;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>A7: avisos por push de Attendance (rechazos al staff, entrada con documento al miembro).</summary>
public sealed class AttendancePushTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public async Task A_document_entry_notifies_the_member_by_push_and_email_with_the_local_time_and_only_once()
    {
        var world = await SeedAsync();
        var token = NewToken();
        await RegisterAsync(world.GymId, world.UserId, token, UserRole.Member, "web_member");
        var push = factory.Services.GetRequiredService<CapturingPushSender>();
        var attendanceId = Guid.CreateVersion7();
        var checkedInAt = AtLocal(Today, 6, 12);
        var evt = new DocumentCheckInRegistered(attendanceId, world.GymId, world.MemberId, checkedInAt);

        await PublishAsync(world.GymId, evt);
        await PublishAsync(world.GymId, evt); // el evento se repite: no duplica

        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var notice = await db.Set<Notification>().SingleAsync(n =>
                n.RecipientMemberId == world.MemberId && n.Type == NotificationType.DocumentCheckIn);
            Assert.Equal(NotificationCategory.Security, notice.Category);
            Assert.Equal(attendanceId, notice.ReferenceId);
            Assert.Equal("Se registró tu entrada con tu documento a las 6:12", notice.Title);
            Assert.Equal(1, await db.Set<NotificationDelivery>().CountAsync(
                d => d.NotificationId == notice.Id && d.Channel == DeliveryChannel.Email));
        }

        await RunDispatcherAtAsync(AtLocal(Today, 6, 13));
        var sent = Assert.Single(push.SentTo(token));
        Assert.Equal("Se registró tu entrada con tu documento a las 6:12", sent.Title);
    }

    [Fact]
    public async Task The_document_entry_notice_ignores_the_members_preferences_and_the_quiet_hours()
    {
        var world = await SeedAsync();
        var token = NewToken();
        await RegisterAsync(world.GymId, world.UserId, token, UserRole.Member, "web_member");
        using (var scope = Scope(world.GymId, world.UserId, UserRole.Member))
        {
            await scope.ServiceProvider.GetRequiredService<UpdateMyNotificationPreferencesHandler>().HandleAsync(
                new UpdateNotificationPreferencesRequest(
                [
                    new NotificationPreferenceUpdate("Membership", false, false),
                    new NotificationPreferenceUpdate("Classes", false, false),
                    new NotificationPreferenceUpdate("Progress", false, false),
                    new NotificationPreferenceUpdate("News", false, false),
                    new NotificationPreferenceUpdate("Promotions", false, false),
                ]), default);
        }

        var push = factory.Services.GetRequiredService<CapturingPushSender>();
        var night = AtLocal(Today, 3);
        await PublishAsync(world.GymId, new DocumentCheckInRegistered(Guid.CreateVersion7(), world.GymId, world.MemberId, night));
        await RunDispatcherAtAsync(night.AddMinutes(1));

        Assert.Single(push.SentTo(token));
    }

    [Fact]
    public async Task A_manual_or_qr_entry_does_not_create_the_document_notice()
    {
        var world = await SeedAsync();
        await PublishAsync(world.GymId, new CheckInRegistered(Guid.CreateVersion7(), world.GymId, world.MemberId, Today));

        using var scope = System();
        Assert.False(await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .AnyAsync(n => n.RecipientMemberId == world.MemberId && n.Type == NotificationType.DocumentCheckIn));
    }

    [Fact]
    public async Task A_rejected_entry_pushes_only_to_staff_who_turned_it_on_and_the_owner_is_not_forced()
    {
        var world = await SeedAsync();
        var asking = Guid.CreateVersion7();
        var silent = Guid.CreateVersion7();
        var askingToken = NewToken();
        var silentToken = NewToken();
        await RegisterAsync(world.GymId, asking, askingToken, UserRole.Staff, "web_panel");
        await RegisterAsync(world.GymId, silent, silentToken, UserRole.Owner, "web_panel");
        using (var scope = Scope(world.GymId, asking, UserRole.Staff))
        {
            await scope.ServiceProvider.GetRequiredService<UpdateMyStaffPushPreferencesHandler>().HandleAsync(
                new UpdateStaffPushPreferencesRequest([new StaffPushPreferenceUpdate("CheckinRejected", true)]), default);
        }

        var push = factory.Services.GetRequiredService<CapturingPushSender>();
        await PublishAsync(world.GymId, new CheckInRejected(
            Guid.CreateVersion7(), world.GymId, world.MemberId, CheckinRejectionReason.NoMembership, 1, null));
        await RunDispatcherAtAsync(AtLocal(Today, 12));

        var sent = Assert.Single(push.SentTo(askingToken));
        Assert.Equal("Entrada rechazada", sent.Title);
        Assert.Empty(push.SentTo(silentToken)); // por defecto apagado, también para el Owner
    }

    // ---------- helpers ----------

    private static string NewToken() => $"tok-{Guid.NewGuid():N}";

    private static DateTimeOffset AtLocal(DateOnly day, int hour, int minute = 0)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(hour).AddMinutes(minute);

    private async Task PublishAsync<TEvent>(Guid gymId, TEvent evt) where TEvent : IDomainEvent
    {
        using var scope = Scope(gymId, Guid.CreateVersion7(), UserRole.Staff);
        await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().PublishAsync(evt, default);
    }

    private async Task RunDispatcherAtAsync(DateTimeOffset instant)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(
            instant, DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(-5)).DateTime));
        await scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>().RunOnceAsync();
    }

    private async Task RegisterAsync(Guid gymId, Guid userId, string token, UserRole role, string platform)
    {
        using var scope = Scope(gymId, userId, role);
        await scope.ServiceProvider.GetRequiredService<RegisterPushDeviceHandler>()
            .HandleAsync(new RegisterPushDeviceRequest(token, platform), default);
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Scope(Guid gymId, Guid userId, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("A7 Gym", $"a7-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        var userId = Guid.CreateVersion7();
        var member = GSM.Domain.Members.Member.Create(
            gym.Id, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-60),
            email: $"m-{Guid.NewGuid():N}@test.local");
        member.LinkUser(userId);
        db.Add(member);
        await db.SaveChangesAsync();
        return new World(gym.Id, member.Id, userId);
    }

    private sealed record World(Guid GymId, Guid MemberId, Guid UserId);
}
