using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

public sealed class UpdateMemberHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    IIdentityReader identity,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    ILogger<UpdateMemberHandler> logger)
{
    public async Task<MemberDetail> HandleAsync(
        Guid memberId, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        var profile = MemberMapper.ToProfile(request);
        var documentNumber = profile.DocumentNumber.Trim();
        if (await members.DocumentExistsAsync(profile.DocumentType, documentNumber, excludeMemberId: memberId, cancellationToken))
        {
            throw new ConflictException($"Ya hay otro miembro con el documento '{documentNumber}'.");
        }

        if (profile.ReferredByMemberId is { } referrerId
            && referrerId != member.ReferredByMemberId
            && !await members.ExistsAsync(referrerId, cancellationToken))
        {
            throw new ValidationDomainException("El miembro que lo refirió no existe en este gimnasio.");
        }

        var today = clock.Today;
        var fieldChanges = member.UpdateProfile(profile, request.JoinedAt, today);
        changes.Record(member, fieldChanges);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Miembro {MemberId} actualizado ({ChangedFields} campos)", memberId, fieldChanges.Count);

        var detail = MemberMapper.ToDetail(member, today);
        return member.TrainerUserId is { } trainerId
            ? detail with { TrainerName = (await identity.GetUserAsync(trainerId, cancellationToken))?.FullName }
            : detail;
    }
}
