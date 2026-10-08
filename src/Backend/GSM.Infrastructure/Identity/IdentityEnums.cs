using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Identity;

/// <summary>Enums nativos de Postgres del módulo Identity.</summary>
public sealed class IdentityEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Identity.UserRole>("user_role", "identity", LowerCaseNameTranslator.Instance),
    ];
}
