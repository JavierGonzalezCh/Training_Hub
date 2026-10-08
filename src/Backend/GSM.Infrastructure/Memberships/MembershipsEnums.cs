using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Memberships;

/// <summary>Enums nativos de Postgres del módulo Memberships.</summary>
public sealed class MembershipsEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Memberships.MembershipStatus>("membership_status", "memberships", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Memberships.RenewalKind>("renewal_kind", "memberships", SnakeCaseNameTranslator.Instance),
    ];
}
