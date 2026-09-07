using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;

namespace GSM.Application.Members;

public sealed class GetMemberHandler(ICurrentUserService currentUser, IMemberRepository members)
{
    public async Task<MemberDetail> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        // Un miembro solo puede ver su propia ficha; owner/staff ven cualquiera de su gym.
        if (currentUser.Role == UserRole.Member && member.UserId != currentUser.UserId)
        {
            throw new ForbiddenDomainException("No puedes ver la ficha de otro miembro.");
        }

        return MemberMapper.ToDetail(member);
    }
}
