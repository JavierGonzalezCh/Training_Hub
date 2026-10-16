using GSM.Application.Attendance.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Memberships.Abstractions;
using GSM.Domain.Memberships;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Memberships;

/// <summary>
/// MS5 (Memberships v2, §7): cada entrada registrada consume una entrada de la tiquetera del miembro, sin
/// que Attendance escriba en Memberships. Idempotente: el consumo es único por entrada de asistencia
/// (índice único), así que reprocesar el evento no descuenta dos veces.
/// <para>
/// Si no hay con qué consumir (sin tiquetera, o sin entradas porque recepción lo dejó pasar), no pasa
/// nada: el error favorece al miembro, nunca lo perjudica. Si una membresía por tiempo lo cubre ese día,
/// la tiquetera no se toca (el letrero y el acceso también la ignoran).
/// </para>
/// </summary>
public sealed class ConsumePassEntryOnCheckInHandler(
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    ILogger<ConsumePassEntryOnCheckInHandler> logger) : IDomainEventHandler<CheckInRegistered>
{
    public async Task HandleAsync(CheckInRegistered domainEvent, CancellationToken cancellationToken = default)
    {
        if (await memberships.GetPassUsageByAttendanceAsync(domainEvent.AttendanceId, cancellationToken) is not null)
        {
            return;
        }

        var date = domainEvent.AttendanceDate;
        var passes = await memberships.GetPassesCoveringAsync(domainEvent.MemberId, date, cancellationToken);
        if (passes.Count == 0
            || await memberships.HasTimeMembershipCoveringAsync(domainEvent.MemberId, date, cancellationToken))
        {
            return;
        }

        var ids = passes.Select(p => p.Id).ToList();
        var freezes = await memberships.GetFreezesAsync(ids, cancellationToken);
        var used = await memberships.GetPassUsedCountsAsync(ids, cancellationToken);

        // Entre varias vigentes con entradas, la que vence primero (la lista ya viene por fin ascendente).
        var pass = passes.FirstOrDefault(p =>
            !freezes.Any(f => f.MembershipId == p.Id && f.CoversDate(date))
            && (p.EntriesTotal ?? 0) - used.GetValueOrDefault(p.Id) > 0);
        if (pass is null)
        {
            logger.LogInformation(
                "La entrada {AttendanceId} del miembro {MemberId} no descuenta tiquetera: no hay entradas disponibles",
                domainEvent.AttendanceId, domainEvent.MemberId);
            return;
        }

        memberships.Add(PassUsage.Create(domainEvent.GymId, pass.Id, domainEvent.AttendanceId, date));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Entrada {AttendanceId} descontó una entrada de la tiquetera {MembershipId} ({Left} restantes)",
            domainEvent.AttendanceId, pass.Id, (pass.EntriesTotal ?? 0) - used.GetValueOrDefault(pass.Id) - 1);
    }
}

/// <summary>
/// Una entrada anulada devuelve la entrada de la tiquetera (§7). Idempotente: si no hay consumo (no
/// usaba tiquetera, o ya se devolvió) no hace nada.
/// </summary>
public sealed class RestorePassEntryOnCheckInVoidedHandler(
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    ILogger<RestorePassEntryOnCheckInVoidedHandler> logger) : IDomainEventHandler<CheckInVoided>
{
    public async Task HandleAsync(CheckInVoided domainEvent, CancellationToken cancellationToken = default)
    {
        var usage = await memberships.GetPassUsageByAttendanceAsync(domainEvent.AttendanceId, cancellationToken);
        if (usage is null)
        {
            return;
        }

        memberships.Remove(usage);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Entrada {AttendanceId} anulada: se devolvió una entrada a la tiquetera {MembershipId}",
            domainEvent.AttendanceId, usage.MembershipId);
    }
}
