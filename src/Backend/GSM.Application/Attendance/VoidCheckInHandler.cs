using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Attendance;

/// <summary>
/// Anula una entrada (borrado lógico con motivo). El permiso <c>ManageAttendance</c> se exige en el
/// controller. Solo entradas de los últimos 7 días de negocio: más atrás ya no es una corrección de recepción.
/// </summary>
public sealed class VoidCheckInHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IAttendanceRepository attendance,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<VoidCheckInHandler> logger)
{
    /// <summary>Días hacia atrás (hoy es el día 0) en que todavía se puede anular.</summary>
    public const int MaxAgeDays = 7;

    public async Task HandleAsync(Guid attendanceId, VoidCheckInRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        var record = await attendance.GetByIdAsync(attendanceId, cancellationToken)
            ?? throw new NotFoundException("Entrada no encontrada.");

        if (record.AttendanceDate < clock.Today.AddDays(-MaxAgeDays))
        {
            throw new ConflictException($"Solo se pueden anular entradas de los últimos {MaxAgeDays} días.");
        }

        var now = clock.UtcNow;
        record.Void(userId, request.Reason, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await events.PublishAsync(new CheckInVoided(
            record.Id, gymId, record.MemberId, record.AttendanceDate, record.CheckedInAt, record.Method,
            userId, now, record.VoidReason!), cancellationToken);

        logger.LogInformation(
            "Entrada {AttendanceId} del miembro {MemberId} anulada por {UserId}", record.Id, record.MemberId, userId);
    }
}
