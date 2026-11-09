// Los tipos que usaban la bandeja y la cola de correos de Comunicaciones antes de N1. Las migraciones
// históricas (y sus Designer) los nombran; ya no son parte del modelo. Se conservan solo para que
// compilen, no para usarse en código nuevo.
namespace GSM.Infrastructure.Migrations;

internal enum LegacyNotificationType
{
    ClassCancelled,
    MembershipExpiring,
    AchievementUnlocked,
}

internal enum LegacyEmailKind
{
    Announcement,
    ClassCancelled,
    MembershipExpiring,
}

internal enum LegacyEmailStatus
{
    Pending,
    Sent,
    Failed,
}

// C4: el disparador del conteo del día sin registro en la puerta se eliminó; las migraciones históricas lo nombran.
internal enum TrainingEntryTrigger
{
    Start,
    FirstLog,
}
