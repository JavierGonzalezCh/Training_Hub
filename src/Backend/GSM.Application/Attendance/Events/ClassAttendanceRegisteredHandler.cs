using GSM.Application.Attendance.Abstractions;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Domain.Attendance;
using Microsoft.Extensions.Logging;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Application.Attendance.Events;

/// <summary>
/// Asistir a una clase cuenta como check-in del día. El check-in ya es idempotente por día desde
/// la etapa 6: si el miembro había fichado al entrar, el registro existe y no se duplica.
/// </summary>
public sealed class ClassAttendanceRegisteredHandler(
    IClock clock,
    IAttendanceRepository attendance,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<ClassAttendanceRegisteredHandler> logger) : IDomainEventHandler<ClassAttendanceRegistered>
{
    public async Task HandleAsync(ClassAttendanceRegistered domainEvent, CancellationToken cancellationToken = default)
    {
        var existing = await attendance.GetForMemberOnDateAsync(
            domainEvent.MemberId, domainEvent.SessionDate, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var record = AttendanceEntity.Create(
            domainEvent.GymId, domainEvent.MemberId, AttendanceMethod.Manual,
            clock.UtcNow, domainEvent.SessionDate);
        attendance.Add(record);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Como cualquier otra entrada: Memberships descuenta la entrada de una tiquetera (MS5, §7). Si el
        // miembro ya había fichado ese día no se llega aquí, así que no se consume dos veces.
        await events.PublishAsync(
            new CheckInRegistered(record.Id, record.GymId, record.MemberId, record.AttendanceDate), cancellationToken);
        logger.LogInformation(
            "Check-in del día {Date} registrado para el miembro {MemberId} por asistencia a clase",
            domainEvent.SessionDate, domainEvent.MemberId);
    }
}
