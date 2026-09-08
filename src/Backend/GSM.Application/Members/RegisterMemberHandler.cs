using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Application.Members.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>
/// Canje anónimo de la invitación: valida el código + documento, crea el <c>AppUser</c> (rol member),
/// vincula el miembro y marca la invitación como usada — todo en una transacción.
/// </summary>
public sealed class RegisterMemberHandler(
    IMemberRepository members,
    IUserProvisioning userProvisioning,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<RegisterMemberHandler> logger)
{
    public async Task<RegisterMemberResponse> HandleAsync(
        RegisterMemberRequest request, CancellationToken cancellationToken)
    {
        var code = InvitationCode.Normalize(request.Code);
        var invitation = await members.GetInvitationByCodeAsync(code, cancellationToken)
            ?? throw new NotFoundException("Código de invitación no encontrado.");

        var now = DateTimeOffset.UtcNow;
        if (!invitation.IsPendingAt(now))
        {
            throw new ConflictException("La invitación ya se usó o expiró.");
        }

        var member = await members.GetByIdAsync(invitation.MemberId, cancellationToken)
            ?? throw new NotFoundException("El miembro de la invitación ya no existe.");

        if (member.Status == MemberStatus.Inactive)
        {
            throw new ConflictException("El miembro está inactivo.");
        }

        if (member.UserId is not null)
        {
            throw new ConflictException("El miembro ya tiene una cuenta.");
        }

        if (!string.Equals(member.DocumentNumber, request.DocumentNumber.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenDomainException("El documento no coincide con la invitación.");
        }

        var userId = Guid.Empty;

        await unitOfWork.ExecuteInTransactionAsync(async innerToken =>
        {
            userId = await userProvisioning.CreateGymUserAsync(
                request.Email.Trim(),
                $"{member.FirstName} {member.LastName}",
                request.Password,
                UserRole.Member,
                member.GymId,
                innerToken);

            member.LinkUser(userId);
            invitation.Redeem(userId, now);

            await unitOfWork.SaveChangesAsync(innerToken);
        }, cancellationToken);

        logger.LogInformation("Miembro {MemberId} registró la cuenta {UserId}", member.Id, userId);
        await events.PublishAsync(
            new MemberRegistered(
                member.Id, userId, member.GymId, request.Email.Trim(),
                $"{member.FirstName} {member.LastName}"),
            cancellationToken);

        return new RegisterMemberResponse(member.Id, userId);
    }
}
