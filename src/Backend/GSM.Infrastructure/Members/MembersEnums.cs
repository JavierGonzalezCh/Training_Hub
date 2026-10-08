using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Members;

/// <summary>Enums nativos de Postgres del módulo Members.</summary>
public sealed class MembersEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Members.MemberStatus>("member_status", "members", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Members.DocumentType>("document_type", "members", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Members.Gender>("gender", "members", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Members.MemberSource>("member_source", "members", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Members.MemberGoal>("member_goal", "members", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Members.MemberChangeSource>("member_change_source", "members", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Members.MemberChangeRole>("member_change_role", "members", SnakeCaseNameTranslator.Instance),
    ];
}
