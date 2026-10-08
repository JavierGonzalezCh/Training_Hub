using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Identity;

/// <summary>Enums nativos de Postgres del módulo Identity.</summary>
public sealed class IdentityEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Identity.UserRole>("user_role", "identity", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Identity.StaffProfile>("staff_profile", "identity", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Identity.StaffPermission>("staff_permission", "identity", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<StaffAccessAuditAction>("staff_access_audit_action", "identity", SnakeCaseNameTranslator.Instance),
    ];
}
