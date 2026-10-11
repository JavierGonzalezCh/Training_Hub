using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Application.Attendance;

/// <summary>
/// Registra la entrada. El miembro solo entra con un código válido del gym (QR de la entrada) y
/// sujeto al veredicto de acceso de Memberships (gracia incluida); el staff registra a mano, sin
/// código, y fuera de la gracia debe "dejar pasar" con motivo.
/// Ver docs/CHECKIN-QR-BUILD-PLAN.md.
/// </summary>
public sealed class CheckInHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMembershipReader memberships,
    IGymReader gyms,
    IAchievementService achievements,
    IAttendanceRepository attendance,
    ICheckinSettingsRepository settingsRepository,
    ICheckinAttemptRepository attempts,
    ICheckinCodeSigner signer,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CheckInHandler> logger)
{
    public async Task<(CheckInResponse Response, bool Created)> HandleAsync(
        CheckInRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var isMember = currentUser.Role == UserRole.Member;
        var member = await ResolveMemberAsync(request, isMember, cancellationToken);

        var settings = await settingsRepository.GetAsync(gymId, cancellationToken)
            ?? CheckinSettings.Default(gymId);

        if (isMember)
        {
            var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
            if (flags is null || !flags.CheckinQr)
            {
                throw new ForbiddenDomainException("Tu gimnasio no tiene activado el check-in con QR.");
            }
        }

        // Gym cerrado según su horario: va antes que todo lo demás y no admite "Dejar pasar".
        await EnsureGymOpenAsync(gymId, cancellationToken);

        if (member.Status != MemberStatus.Active)
        {
            throw new ConflictException("El miembro está inactivo.");
        }

        if (isMember
            && !signer.Validate(gymId, request.GymCode, settings.Mode, settings.StaticCodeVersion, clock.UtcNow))
        {
            throw new ValidationDomainException("El código QR no es válido o ya caducó. Escanea de nuevo.");
        }

        var today = clock.Today;
        var existing = await attendance.GetForMemberOnDateAsync(member.Id, today, cancellationToken);

        // Memberships decide quién entra (estado, gracia y letrero): Attendance ya no calcula la gracia.
        var access = await memberships.GetAccessStatusAsync(member.Id, cancellationToken);

        // Quien ya entró hoy no vuelve a ser rechazado, aunque su membresía haya vencido después.
        string? authorizationReason = null;
        if (existing is null && access.Kind == AccessKind.Blocked)
        {
            if (isMember)
            {
                await RejectAsync(gymId, member, access, today, cancellationToken);
            }

            authorizationReason = RequireAuthorizationReason(access, request.AuthorizationReason);
        }

        var created = existing is null;
        AttendanceEntity record;
        if (existing is not null)
        {
            record = existing;
        }
        else
        {
            var method = isMember ? AttendanceMethod.Qr : AttendanceMethod.Manual;
            record = AttendanceEntity.Create(
                gymId, member.Id, method, clock.UtcNow, today,
                authorizationReason is null ? null : currentUser.UserId,
                authorizationReason);
            attendance.Add(record);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await events.PublishAsync(new CheckInRegistered(record.Id, gymId, member.Id), cancellationToken);
            logger.LogInformation(
                "Check-in {AttendanceId} de miembro {MemberId}{Authorized}",
                record.Id, member.Id, record.AuthorizedBy is null ? string.Empty : " (entrada autorizada)");
        }

        var unlocked = await achievements.EvaluateForMemberAsync(member.Id, cancellationToken);

        var covered = access.Kind is AccessKind.Covered or AccessKind.Expiring;
        var response = new CheckInResponse(
            record.Id,
            member.Id,
            $"{member.FirstName} {member.LastName}",
            record.CheckedInAt,
            record.Method.ToString(),
            AlreadyCheckedInToday: !created,
            MembershipExpired: !covered,
            unlocked,
            MembershipEndsOn: covered ? null : access.EndsOn,
            GraceDaysLeft: access.GraceDaysLeft,
            Notice: access.Notice,
            DaysLeft: access.DaysLeft,
            Authorized: record.AuthorizedBy is not null);

        return (response, created);
    }

    private async Task EnsureGymOpenAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var status = await gyms.GetOpenStatusAsync(gymId, clock.UtcNow, cancellationToken);
        if (!status.IsOpen)
        {
            var today = DateOnly.FromDateTime(clock.ToBusinessLocal(clock.UtcNow));
            logger.LogInformation("Check-in rechazado en el gym {GymId}: gym_closed", gymId);
            throw new ConflictException(OpenStatusText.ClosedMessage(status, today));
        }
    }

    /// <summary>
    /// "Dejar pasar": el staff puede registrar a mano a quien no puede entrar, pero con un motivo
    /// obligatorio que queda en la asistencia junto con quién lo autorizó.
    /// </summary>
    private static string RequireAuthorizationReason(AccessStatus access, string? reason)
    {
        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is < 3 or > 500)
        {
            throw new ValidationDomainException(
                $"{access.BlockMessage} Para dejarlo pasar indica un motivo (3 a 500 caracteres).");
        }

        return trimmed;
    }

    private async Task RejectAsync(
        Guid gymId, MemberInfo member, AccessStatus access, DateOnly today, CancellationToken cancellationToken)
    {
        var endedOn = access.BlockReason == AccessBlockReason.Expired ? access.EndsOn : null;
        var reason = endedOn is null ? CheckinRejectionReason.NoMembership : CheckinRejectionReason.MembershipExpired;
        var now = clock.UtcNow;

        // Se guarda antes de lanzar: cada SaveChanges abre su propia transacción, así que no se revierte.
        var attempt = await attempts.GetPendingForMemberOnDateAsync(member.Id, today, cancellationToken);
        if (attempt is null)
        {
            attempt = CheckinAttempt.Create(gymId, member.Id, reason, now, today, endedOn);
            attempts.Add(attempt);
        }
        else
        {
            attempt.Repeat(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new CheckInRejected(
            attempt.Id, gymId, member.Id, reason, attempt.AttemptCount, attempt.MembershipEndedOn), cancellationToken);
        logger.LogInformation(
            "Check-in rechazado de miembro {MemberId}: {Reason} (intento {AttemptId}, x{Count})",
            member.Id, reason, attempt.Id, attempt.AttemptCount);

        throw new ConflictException(
            access.BlockReason == AccessBlockReason.Frozen
                ? "Tu membresía está congelada. Pasa por recepción."
                : endedOn is { } ended
                    ? $"Tu membresía venció el {ended:dd/MM/yyyy}. Pasa por recepción para renovarla."
                    : "No tienes una membresía activa. Pasa por recepción.");
    }

    private async Task<MemberInfo> ResolveMemberAsync(
        CheckInRequest request, bool isMember, CancellationToken cancellationToken)
    {
        if (isMember)
        {
            if (string.IsNullOrWhiteSpace(request.GymCode))
            {
                throw new ValidationDomainException("Escanea el QR de la entrada del gimnasio para registrar tu entrada.");
            }

            var userId = currentUser.UserId
                ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
            return await members.ResolveByUserIdAsync(userId, cancellationToken)
                ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");
        }

        if (request.MemberId is not { } memberId)
        {
            throw new ValidationDomainException("Indica el memberId.");
        }

        return await members.GetAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");
    }
}
