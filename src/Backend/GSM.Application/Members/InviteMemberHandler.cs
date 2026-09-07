using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>Emite un código de invitación para que el miembro cree su cuenta de la app.</summary>
public sealed class InviteMemberHandler(
    ICurrentUserService currentUser,
    IMemberRepository members,
    IUnitOfWork unitOfWork,
    ILogger<InviteMemberHandler> logger)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public async Task<InviteMemberResponse> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        if (member.Status == MemberStatus.Inactive)
        {
            throw new ConflictException("No se puede invitar a un miembro inactivo.");
        }

        if (member.UserId is not null)
        {
            throw new ConflictException("El miembro ya tiene una cuenta vinculada.");
        }

        await members.RemovePendingInvitationsAsync(memberId, cancellationToken);

        string code;
        do
        {
            code = InvitationCode.New();
        }
        while (await members.InvitationCodeExistsAsync(code, cancellationToken));

        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        members.Add(MemberInvitation.Issue(memberId, gymId, code, expiresAt));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Invitación emitida para el miembro {MemberId}", memberId);
        return new InviteMemberResponse(code, expiresAt);
    }
}
