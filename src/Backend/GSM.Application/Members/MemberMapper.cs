using GSM.Application.Members.Contracts;
using GSM.Domain.Members;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Members;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class MemberMapper
{
    public static partial MemberDetail ToDetail(Member member);

    public static partial MemberListItem ToListItem(Member member);
}
