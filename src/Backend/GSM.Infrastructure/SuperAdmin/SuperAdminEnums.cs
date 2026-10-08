using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.SuperAdmin;

/// <summary>Enums nativos de Postgres del módulo SuperAdmin.</summary>
public sealed class SuperAdminEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.SuperAdmin.ImpersonationMode>("impersonation_mode", "superadmin", SnakeCaseNameTranslator.Instance),
    ];
}
