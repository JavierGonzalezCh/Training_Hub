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
    IMemberRepository members,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CreateMemberHandler> logger)
{
    public async Task<CreateMemberResponse> HandleAsync(
        CreateMemberRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var documentNumber = request.DocumentNumber.Trim();
        if (await members.DocumentExistsAsync(documentNumber, excludeMemberId: null, cancellationToken))
        {
            throw new ConflictException($"Ya hay un miembro con el documento '{documentNumber}'.");
        }

        var member = Member.Create(
            gymId,
            request.FirstName,
            request.LastName,
            documentNumber,
            DateOnly.FromDateTime(DateTime.UtcNow),
            request.Phone,
            request.Email,
            request.BirthDate,
            request.EmergencyContact,
            request.PhotoUrl);

        members.Add(member);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Miembro {MemberId} creado en gym {GymId}", member.Id, gymId);
        await events.PublishAsync(new MemberCreated(member.Id, gymId), cancellationToken);

        return new CreateMemberResponse(member.Id, member.QrCode);
    }
}
