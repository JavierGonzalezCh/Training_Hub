using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Communications;

/// <summary>Enums nativos de Postgres del módulo Communications.</summary>
public sealed class CommunicationsEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Communications.AnnouncementStatus>("announcement_status", "communications", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Communications.ReactionKind>("reaction_kind", "communications", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Communications.NotificationType>("notification_type", "communications", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Communications.EmailKind>("email_kind", "communications", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Communications.EmailStatus>("email_status", "communications", LowerCaseNameTranslator.Instance),
    ];
}
