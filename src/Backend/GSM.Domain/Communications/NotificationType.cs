namespace GSM.Domain.Communications;

/// <summary>
/// Avisos personales que genera el sistema. Label en Postgres: <c>communications.notification_type</c>,
/// con <c>SnakeCaseNameTranslator</c> (<c>class_cancelled</c>, <c>membership_expiring</c>, …).
/// </summary>
public enum NotificationType
{
    ClassCancelled,
    MembershipExpiring,
    AchievementUnlocked,
}
