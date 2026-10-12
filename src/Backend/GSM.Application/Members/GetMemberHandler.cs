using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;

namespace GSM.Application.Members;

public sealed class GetMemberHandler(ICurrentUserService currentUser, IClock clock, IMemberRepository members, IIdentityReader identity)
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

        var detail = MemberMapper.ToDetail(member, clock.Today);
        if (member.TrainerUserId is { } trainerId)
        {
            detail = detail with { TrainerName = (await identity.GetUserAsync(trainerId, cancellationToken))?.FullName };
        }

        // Source, Tags y el referente son solo del staff (Members v2, §3.1): el miembro no los ve.
        return currentUser.Role == UserRole.Member
            ? detail with { Source = null, Tags = [], ReferredByMemberId = null }
            : detail;
    }
}
