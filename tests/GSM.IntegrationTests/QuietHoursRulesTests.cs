using GSM.Application.Notifications;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Common;
using Microsoft.Extensions.Options;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// CO6 (spec Comunicaciones §G): reglas puras del horario sin avisos. Qué avisos esperan, cuáles nunca y
/// hasta cuándo, incluido el horario que cruza la medianoche.
/// </summary>
public sealed class QuietHoursRulesTests
{
    private static readonly SystemClock Clock = new(
        Options.Create(new ClockOptions { TimeZone = "America/Bogota" }));

    private static readonly Guid Gym = Guid.CreateVersion7();
    private static readonly DateOnly Day = new(2026, 10, 20);

    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0)
        => Clock.BusinessDayRange(day).Start.AddHours(hour).AddMinutes(minute);

    private static Notification ForMember(
        NotificationType type, NotificationCategory? category, NotificationPriority priority = NotificationPriority.Normal)
        => Notification.CreateForMember(
            Gym, Guid.CreateVersion7(), type, category, Guid.CreateVersion7(), "t", "b", null, At(Day, 12), priority);

    [Theory]
    [InlineData(NotificationType.Announcement, NotificationCategory.News)]
    [InlineData(NotificationType.Announcement, NotificationCategory.Promotions)]
    [InlineData(NotificationType.Birthday, NotificationCategory.News)]
    public void News_promotions_and_birthdays_are_held(NotificationType type, NotificationCategory category)
        => Assert.True(QuietHours.Applies(ForMember(type, category)));

    [Fact]
    public void Class_reminders_waitlist_offers_security_important_and_progress_notices_are_exempt()
    {
        Assert.False(QuietHours.Applies(ForMember(NotificationType.ClassReminder, NotificationCategory.Classes)));
        Assert.False(QuietHours.Applies(ForMember(
            NotificationType.WaitlistOffer, NotificationCategory.Classes, NotificationPriority.Urgent)));
        Assert.False(QuietHours.Applies(ForMember(NotificationType.ClassCancelled, NotificationCategory.Classes)));
        Assert.False(QuietHours.Applies(ForMember(NotificationType.MembershipExpiring, NotificationCategory.Membership)));
        Assert.False(QuietHours.Applies(ForMember(NotificationType.AchievementUnlocked, NotificationCategory.Progress)));
        Assert.False(QuietHours.Applies(ForMember(NotificationType.ImportantAnnouncement, NotificationCategory.News)));
        Assert.False(QuietHours.Applies(ForMember(NotificationType.DocumentCheckIn, NotificationCategory.Security)));
        // Un aviso urgente nunca espera, aunque sea de una categoría que normalmente espera.
        Assert.False(QuietHours.Applies(ForMember(
            NotificationType.Announcement, NotificationCategory.News, NotificationPriority.Urgent)));
    }

    [Fact]
    public void Staff_notices_never_wait()
    {
        var action = Notification.CreateGymAction(
            Gym, NotificationType.CheckinRejected, Guid.CreateVersion7(), "t", "b", null, At(Day, 12));
        var personal = Notification.CreateForUser(
            Gym, Guid.CreateVersion7(), NotificationType.InstructorClassReminder, Guid.CreateVersion7(), "t", "b", null, At(Day, 12));
        Assert.False(QuietHours.Applies(action));
        Assert.False(QuietHours.Applies(personal));
    }

    [Theory]
    [InlineData(21, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(20, 59, false)]
    public void The_default_window_crosses_midnight(int hour, int minute, bool quiet)
        => Assert.Equal(quiet, QuietHours.IsQuietNow(
            NotificationSettings.Default(Gym), Clock, At(Day, hour, minute)));

    [Fact]
    public void A_disabled_schedule_is_never_quiet()
    {
        var settings = NotificationSettings.Default(Gym);
        settings.Update(false, new TimeOnly(21, 0), new TimeOnly(7, 0));
        Assert.False(QuietHours.IsQuietNow(settings, Clock, At(Day, 3)));
    }

    [Fact]
    public void A_held_notice_is_released_at_the_end_of_the_window_of_that_night()
    {
        var settings = NotificationSettings.Default(Gym);

        // 22:00: sale al día siguiente a las 7:00.
        Assert.Equal(At(Day.AddDays(1), 7), QuietHours.NextEnd(settings, Clock, At(Day, 22)));
        // 01:00 de madrugada: sale esa misma mañana a las 7:00, no un día después.
        Assert.Equal(At(Day, 7), QuietHours.NextEnd(settings, Clock, At(Day, 1)));
        Assert.Equal(At(Day, 7), QuietHours.NextEnd(settings, Clock, At(Day, 6, 59)));
    }

    [Fact]
    public void A_window_inside_the_same_day_releases_the_same_day()
    {
        var settings = NotificationSettings.Default(Gym);
        settings.Update(true, new TimeOnly(13, 0), new TimeOnly(15, 0));

        Assert.True(QuietHours.IsQuietNow(settings, Clock, At(Day, 14)));
        Assert.False(QuietHours.IsQuietNow(settings, Clock, At(Day, 15)));
        Assert.Equal(At(Day, 15), QuietHours.NextEnd(settings, Clock, At(Day, 13, 30)));
    }
}
