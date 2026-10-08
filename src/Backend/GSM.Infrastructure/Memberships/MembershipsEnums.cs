using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Memberships;

/// <summary>Enums nativos de Postgres del módulo Memberships.</summary>
public sealed class MembershipsEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Memberships.MembershipStatus>("membership_status", "memberships", LowerCaseNameTranslator.Instance),
    ];
}
