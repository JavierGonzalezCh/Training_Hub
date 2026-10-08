using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Members;

/// <summary>Enums nativos de Postgres del módulo Members.</summary>
public sealed class MembersEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Members.MemberStatus>("member_status", "members", LowerCaseNameTranslator.Instance),
    ];
}
