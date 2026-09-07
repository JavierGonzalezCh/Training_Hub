using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

public sealed class UpdateMemberHandler(
    ICurrentUserService currentUser,
    IMemberRepository members,
    IUnitOfWork unitOfWork,
    ILogger<UpdateMemberHandler> logger)
{
    public async Task<MemberDetail> HandleAsync(
        Guid memberId, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        var documentNumber = request.DocumentNumber.Trim();
        if (await members.DocumentExistsAsync(documentNumber, excludeMemberId: memberId, cancellationToken))
        {
            throw new ConflictException($"Ya hay otro miembro con el documento '{documentNumber}'.");
        }

        member.UpdateProfile(
            request.FirstName,
            request.LastName,
            documentNumber,
            request.Phone,
            request.Email,
            request.BirthDate,
            request.EmergencyContact,
            request.PhotoUrl);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Miembro {MemberId} actualizado", memberId);

        return MemberMapper.ToDetail(member);
    }
}
