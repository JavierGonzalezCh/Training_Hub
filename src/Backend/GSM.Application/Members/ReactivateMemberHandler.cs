using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>
/// Devuelve a activo a un miembro dado de baja (Members v2, §5). Un miembro que ya está activo es un
/// choque de estado (409). Deja su fila en el historial de cambios.
/// </summary>
public sealed class ReactivateMemberHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    ILogger<ReactivateMemberHandler> logger)
{
    public async Task<MemberDetail> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        member.Reactivate();
        changes.RecordStatus(member, MemberStatus.Inactive, MemberStatus.Active);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Miembro {MemberId} reactivado", memberId);
        return MemberMapper.ToDetail(member, clock.Today);
    }
}
