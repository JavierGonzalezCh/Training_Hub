using GSM.Application.Members.Contracts;
using GSM.Domain.Members;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Members;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class MemberMapper
{
    // CurrentMembership y RecentPayments las compone el controller (IMembershipReader / IPaymentReader).
    [MapperIgnoreTarget(nameof(MemberDetail.CurrentMembership))]
    [MapperIgnoreTarget(nameof(MemberDetail.RecentPayments))]
    public static partial MemberDetail ToDetail(Member member);

    public static partial MemberListItem ToListItem(Member member);
}
