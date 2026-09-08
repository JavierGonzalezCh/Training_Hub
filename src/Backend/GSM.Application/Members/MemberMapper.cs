using GSM.Application.Members.Contracts;
using GSM.Domain.Members;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Members;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class MemberMapper
{
    // CurrentMembership la compone el controller a partir de IMembershipReader.
    [MapperIgnoreTarget(nameof(MemberDetail.CurrentMembership))]
    public static partial MemberDetail ToDetail(Member member);

    public static partial MemberListItem ToListItem(Member member);
}
