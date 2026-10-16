using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>Baja de un miembro: soft delete a <c>inactive</c>. Idempotente.</summary>
public sealed class DeactivateMemberHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<DeactivateMemberHandler> logger)
{
    public async Task HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        if (member.Status == MemberStatus.Inactive)
        {
            return;
        }

        member.Deactivate(clock.Today);
        changes.RecordStatus(member, MemberStatus.Active, MemberStatus.Inactive);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Miembro {MemberId} desactivado", memberId);
        await events.PublishAsync(new MemberDeactivated(memberId, gymId), cancellationToken);
    }
}
