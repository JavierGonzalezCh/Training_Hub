using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
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
/// sujeto a la regla de gracia; el staff registra a mano, sin código y sin gracia (con aviso).
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

        var active = await memberships.IsActiveAsync(member.Id, cancellationToken);
        var endedOn = await memberships.GetLatestEndDateAsync(member.Id, cancellationToken);
        int? graceLeft = !active && endedOn is { } end && today <= end.AddDays(settings.GraceDays)
            ? end.AddDays(settings.GraceDays).DayNumber - today.DayNumber
            : null;

        // Quien ya entró hoy no vuelve a ser rechazado, aunque su membresía haya vencido después.
        if (existing is null && isMember && !active && graceLeft is null)
        {
            await RejectAsync(gymId, member, endedOn, today, cancellationToken);
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
            record = AttendanceEntity.Create(gymId, member.Id, method, clock.UtcNow, today);
            attendance.Add(record);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await events.PublishAsync(new CheckInRegistered(record.Id, gymId, member.Id), cancellationToken);
            logger.LogInformation("Check-in {AttendanceId} de miembro {MemberId}", record.Id, member.Id);
        }

        var unlocked = await achievements.EvaluateForMemberAsync(member.Id, cancellationToken);

        var response = new CheckInResponse(
            record.Id,
            member.Id,
            $"{member.FirstName} {member.LastName}",
            record.CheckedInAt,
            record.Method.ToString(),
            AlreadyCheckedInToday: !created,
            MembershipExpired: !active,
            unlocked,
            MembershipEndsOn: active ? null : endedOn,
            GraceDaysLeft: graceLeft);

        return (response, created);
    }

    private async Task RejectAsync(
        Guid gymId, MemberInfo member, DateOnly? endedOn, DateOnly today, CancellationToken cancellationToken)
    {
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

        throw new ConflictException(endedOn is { } ended
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
