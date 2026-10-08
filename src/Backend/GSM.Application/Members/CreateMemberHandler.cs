using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Application.Members.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

public sealed class CreateMemberHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CreateMemberHandler> logger)
{
    public async Task<CreateMemberResponse> HandleAsync(
        CreateMemberRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var profile = MemberMapper.ToProfile(request);
        var documentNumber = profile.DocumentNumber.Trim();
        if (await members.DocumentExistsAsync(profile.DocumentType, documentNumber, excludeMemberId: null, cancellationToken))
        {
            throw new ConflictException($"Ya hay un miembro con el documento '{documentNumber}'.");
        }

        if (profile.ReferredByMemberId is { } referrerId && !await members.ExistsAsync(referrerId, cancellationToken))
        {
            throw new ValidationDomainException("El miembro que lo refirió no existe en este gimnasio.");
        }

        var today = clock.Today;
        var member = Member.Create(gymId, profile, request.JoinedAt ?? today, today);

        members.Add(member);
        changes.RecordCreated(member);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Miembro {MemberId} creado en gym {GymId}", member.Id, gymId);
        await events.PublishAsync(new MemberCreated(member.Id, gymId), cancellationToken);

        return new CreateMemberResponse(member.Id);
    }
}
