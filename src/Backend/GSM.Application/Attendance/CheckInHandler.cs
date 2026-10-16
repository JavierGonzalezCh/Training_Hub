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
    IMemberEntryRuleRepository entryRules,
    ICheckinCodeSigner signer,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CheckInHandler> logger)
{
    public async Task<(CheckInResponse Response, bool Created)> HandleAsync(
        CheckInRequest request, CancellationToken cancellationToken)
    {
        var isMember = currentUser.Role == UserRole.Member;
        var member = await ResolveMemberAsync(request, isMember, cancellationToken);
        return await RunAsync(
            request, member, isMember ? EntryChannel.Qr : EntryChannel.Staff, cancellationToken);
    }

    /// <summary>
    /// Entrada con documento en el kiosco (A4): las mismas reglas que el QR (gym abierto, miembro activo, una
    /// entrada por día, veredicto de Memberships), sin código y sin "Dejar pasar". Quien llama (el caso de uso
    /// del kiosco) ya resolvió al miembro y comprobó que el gym lo tiene encendido. Un rechazo deja el intento
    /// y lanza <see cref="ConflictException"/>.
    /// </summary>
    internal async Task<CheckInResponse> HandleDocumentAsync(MemberInfo member, CancellationToken cancellationToken)
    {
        var (response, _) = await RunAsync(
            new CheckInRequest(member.Id, null), member, EntryChannel.Document, cancellationToken);
        return response;
    }

    private enum EntryChannel
    {
        Qr,
        Staff,
        Document,
    }

    private async Task<(CheckInResponse Response, bool Created)> RunAsync(
        CheckInRequest request, MemberInfo member, EntryChannel channel, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var isMember = channel != EntryChannel.Staff;

        var settings = await settingsRepository.GetAsync(gymId, cancellationToken)
            ?? CheckinSettings.Default(gymId);

        if (channel == EntryChannel.Qr)
        {
            var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
            if (flags is null || !flags.CheckinQr)
            {
                throw new ForbiddenDomainException("Tu gimnasio no tiene activado el check-in con QR.");
            }
        }

        // Gym cerrado según su horario: va antes que todo lo demás, no deja intento y no admite "Dejar pasar".
        await EnsureGymOpenAsync(gymId, cancellationToken);

        // Un código inválido es un error de forma del QR, no un intento rechazado.
        if (channel == EntryChannel.Qr
            && !signer.Validate(gymId, request.GymCode, settings.Mode, settings.StaticCodeVersion, clock.UtcNow))
        {
            throw new ValidationDomainException("El código QR no es válido o ya caducó. Escanea de nuevo.");
        }

        var today = clock.Today;

        // Miembro dado de baja: ni QR ni recepción (primero se reactiva la ficha).
        if (member.Status != MemberStatus.Active)
        {
            const string inactive = "El miembro está dado de baja. Reactiva su ficha para registrar su entrada.";
            if (isMember)
            {
                await RejectAsync(gymId, member, CheckinRejectionReason.MemberInactive, null, today, inactive, true, cancellationToken);
            }

            throw new ConflictException(inactive);
        }

        // Una entrada por día, por cualquier vía; el staff puede habilitar entradas múltiples (§3.2):
        // hasta el máximo del día y con 2 horas mínimo entre una y otra.
        // A1b: el registro es idempotente. El candado pone en fila dos escaneos simultáneos del mismo miembro, y un
        // segundo escaneo pegado al primero (doble toque, reintento por mala señal) devuelve la misma entrada en vez
        // de crear otra (que en una tiquetera gastaría dos) o de responder 409.
        await using var memberLock = await attendance.LockMemberAsync(member.Id, cancellationToken);
        var entriesToday = await attendance.GetEntriesOnDateAsync(member.Id, today, cancellationToken);
        if (entriesToday.Count > 0 && clock.UtcNow - entriesToday[^1].CheckedInAt < DuplicateWindow)
        {
            return (await ReplayAsync(entriesToday[^1], member, cancellationToken), false);
        }

        var rule = await entryRules.GetForMemberAsync(member.Id, cancellationToken);
        var allowance = EntryAllowance.Evaluate(entriesToday, rule, today, clock.UtcNow);
        if (!allowance.Allowed)
        {
            var at = clock.ToBusinessLocal(allowance.LastEntryAt!.Value).ToString("H:mm");
            string memberMessage;
            string staffMessage;
            if (allowance.NextEntryAt is { } next)
            {
                var nextAt = clock.ToBusinessLocal(next).ToString("H:mm");
                memberMessage = $"Ya registraste tu entrada hoy a las {at}. Podrás volver a entrar a partir de las {nextAt}.";
                staffMessage = $"{member.FirstName} {member.LastName} ya entró hoy a las {at}. Puede volver a entrar a partir de las {nextAt}.";
            }
            else
            {
                memberMessage = allowance.MultipleEntries
                    ? $"Ya registraste tus {allowance.AllowedToday} entradas de hoy (la última a las {at})."
                    : $"Ya registraste tu entrada hoy a las {at}.";
                staffMessage = allowance.MultipleEntries
                    ? $"{member.FirstName} {member.LastName} ya llegó a su máximo de {allowance.AllowedToday} entradas hoy (la última a las {at})."
                    : $"{member.FirstName} {member.LastName} ya entró hoy a las {at}.";
            }

            if (isMember)
            {
                // Sin evento: este motivo no suena en la campana ni genera push.
                await RejectAsync(
                    gymId, member, CheckinRejectionReason.AlreadyCheckedIn, null, today, memberMessage, false, cancellationToken);
            }

            throw new ConflictException(staffMessage);
        }

        // Memberships decide quién entra (estado, gracia y letrero): Attendance ya no calcula la gracia.
        var access = await memberships.GetAccessStatusAsync(member.Id, cancellationToken);

        string? authorizationReason = null;
        if (access.Kind == AccessKind.Blocked)
        {
            if (isMember)
            {
                var (reason, endedOn, message) = Describe(access);
                await RejectAsync(gymId, member, reason, endedOn, today, message, true, cancellationToken);
            }

            authorizationReason = RequireAuthorizationReason(access, request.AuthorizationReason);
        }

        var method = channel switch
        {
            EntryChannel.Qr => AttendanceMethod.Qr,
            EntryChannel.Document => AttendanceMethod.Document,
            _ => AttendanceMethod.Manual,
        };

        // A6: geocerca solo con el QR fijo y solo si el gym configuró sus coordenadas. Se guarda el resultado,
        // nunca las coordenadas. Fuera del radio o sin ubicación la entrada se registra igual (acceso, tiquetera y
        // ocupación), pero no cuenta para logros ni racha.
        LocationCheck? locationCheck = null;
        if (channel == EntryChannel.Qr && settings.Mode == CheckinMode.Static
            && await gyms.GetGeofenceAsync(gymId, cancellationToken) is { } fence)
        {
            locationCheck = GeoDistance.Evaluate(fence, request.Latitude, request.Longitude);
        }

        var record = AttendanceEntity.Create(
            gymId, member.Id, method, clock.UtcNow, today,
            authorizationReason is null ? null : currentUser.UserId,
            authorizationReason,
            locationCheck);
        attendance.Add(record);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new CheckInRegistered(record.Id, gymId, member.Id, record.AttendanceDate), cancellationToken);
        if (method == AttendanceMethod.Document)
        {
            // A7: Notifications avisa al miembro dueño del documento (push y correo).
            await events.PublishAsync(
                new DocumentCheckInRegistered(record.Id, gymId, member.Id, record.CheckedInAt), cancellationToken);
        }

        logger.LogInformation(
            "Check-in {AttendanceId} de miembro {MemberId}{Authorized} (ubicación: {LocationCheck})",
            record.Id, member.Id, record.AuthorizedBy is null ? string.Empty : " (entrada autorizada)",
            locationCheck?.ToString() ?? "no pedida");

        // Tiquetera (MS5): el evento ya descontó esta entrada; el saldo y el letrero deben reflejarlo
        // ("Te quedan 2 entradas" tras entrar, no antes). Si el manejador falló, queda el saldo anterior.
        var notice = access.Notice;
        var entriesLeft = access.EntriesLeft;
        if (access.EntriesLeft is not null && access.Kind != AccessKind.Blocked)
        {
            var after = await memberships.GetAccessStatusAsync(member.Id, cancellationToken);
            entriesLeft = after.EntriesLeft ?? 0;
            notice = after.Kind == AccessKind.Blocked && after.BlockReason == AccessBlockReason.PassExhausted
                ? "Usaste la última entrada de tu tiquetera."
                : after.Notice;
        }

        // Una entrada que no cuenta para el progreso no desbloquea logros (los lectores de Attendance ya la excluyen).
        IReadOnlyList<UnlockedAchievement> unlocked = record.CountsForProgress
            ? await achievements.EvaluateForMemberAsync(member.Id, cancellationToken)
            : [];

        var response = await BuildResponseAsync(record, member, access, notice, entriesLeft, unlocked, cancellationToken);
        return (response, true);
    }

    /// <summary>Un escaneo repetido dentro de esta ventana devuelve la entrada ya registrada (A1b).</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);

    /// <summary>La respuesta de una entrada que ya existía: mismo registro, estado actual, sin logros nuevos.</summary>
    private async Task<CheckInResponse> ReplayAsync(
        AttendanceEntity record, MemberInfo member, CancellationToken cancellationToken)
    {
        logger.LogInformation("Check-in repetido de miembro {MemberId}: se devuelve la entrada {AttendanceId}", member.Id, record.Id);
        var access = await memberships.GetAccessStatusAsync(member.Id, cancellationToken);
        return await BuildResponseAsync(record, member, access, access.Notice, access.EntriesLeft, [], cancellationToken);
    }

    private async Task<CheckInResponse> BuildResponseAsync(
        AttendanceEntity record, MemberInfo member, AccessStatus access, string? notice, int? entriesLeft,
        IReadOnlyList<UnlockedAchievement> unlocked, CancellationToken cancellationToken)
    {
        var covered = access.Kind is AccessKind.Covered or AccessKind.Expiring;
        return new CheckInResponse(
            record.Id,
            member.Id,
            $"{member.FirstName} {member.LastName}",
            record.CheckedInAt,
            record.Method.ToString(),
            MembershipExpired: !covered,
            unlocked,
            MembershipEndsOn: covered ? null : access.EndsOn,
            GraceDaysLeft: access.GraceDaysLeft,
            Notice: notice,
            DaysLeft: access.DaysLeft,
            Authorized: record.AuthorizedBy is not null,
            MissingWaiver: !await members.HasLiabilityWaiverAsync(member.Id, cancellationToken),
            EntriesLeft: entriesLeft,
            EntriesTotal: access.EntriesTotal,
            LocationCheck: record.LocationCheck?.ToString(),
            CountsForProgress: record.CountsForProgress,
            LocationNotice: GeoDistance.NoticeFor(record.LocationCheck));
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

    private static (CheckinRejectionReason Reason, DateOnly? EndedOn, string Message) Describe(AccessStatus access)
        => access.BlockReason switch
        {
            AccessBlockReason.Frozen => (
                CheckinRejectionReason.MembershipFrozen, null, "Tu membresía está congelada. Pasa por recepción."),
            AccessBlockReason.Scheduled when access.StartsOn is { } starts => (
                // Sin valor propio en el enum de Postgres (evita migrar): se guarda como "sin membresía".
                CheckinRejectionReason.NoMembership, null,
                $"Tu plan empieza el {starts:dd/MM/yyyy}. Pasa por recepción."),
            AccessBlockReason.PassExhausted => (
                CheckinRejectionReason.PassExhausted, null,
                "Ya usaste todas las entradas de tu tiquetera. Pasa por recepción para renovarla."),
            AccessBlockReason.Expired when access.EndsOn is { } ended => (
                CheckinRejectionReason.MembershipExpired, ended,
                $"Tu membresía venció el {ended:dd/MM/yyyy}. Pasa por recepción para renovarla."),
            _ => (CheckinRejectionReason.NoMembership, null, "No tienes una membresía activa. Pasa por recepción."),
        };

    private async Task RejectAsync(
        Guid gymId, MemberInfo member, CheckinRejectionReason reason, DateOnly? endedOn, DateOnly today,
        string message, bool publish, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Se guarda antes de lanzar: cada SaveChanges abre su propia transacción, así que no se revierte.
        var attempt = await attempts.GetPendingForMemberOnDateAsync(member.Id, today, reason, cancellationToken);
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
        if (publish)
        {
            await events.PublishAsync(new CheckInRejected(
                attempt.Id, gymId, member.Id, reason, attempt.AttemptCount, attempt.MembershipEndedOn), cancellationToken);
        }

        logger.LogInformation(
            "Check-in rechazado de miembro {MemberId}: {Reason} (intento {AttemptId}, x{Count})",
            member.Id, reason, attempt.Id, attempt.AttemptCount);

        throw new ConflictException(message);
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
