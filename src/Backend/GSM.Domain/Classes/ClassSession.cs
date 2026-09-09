using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Classes;

/// <summary>
/// Una ocurrencia concreta de una <see cref="GymClass"/>. No se genera por adelantado: se
/// materializa solo cuando hay algo que guardar (una reserva, una marca de asistencia o una
/// cancelación puntual). Los campos <see cref="StartTime"/>, <see cref="DurationMinutes"/>,
/// <see cref="InstructorName"/> y <see cref="Capacity"/> son una <b>copia</b> de la definición en
/// el momento de materializar: si el gym la edita después, el histórico conserva lo que pasó.
/// </summary>
public sealed class ClassSession : BaseEntity, ITenantEntity
{
    private ClassSession()
    {
    }

    public Guid GymId { get; private set; }

    public Guid ClassId { get; private set; }

    public DateOnly SessionDate { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public int DurationMinutes { get; private set; }

    public string? InstructorName { get; private set; }

    public int? Capacity { get; private set; }

    public ClassSessionStatus Status { get; private set; } = ClassSessionStatus.Scheduled;

    public string? CancellationReason { get; private set; }

    public static ClassSession Materialize(
        Guid gymId,
        Guid classId,
        DateOnly sessionDate,
        TimeOnly startTime,
        int durationMinutes,
        string? instructorName,
        int? capacity)
    {
        if (gymId == Guid.Empty || classId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de sesión incompletos.");
        }

        return new ClassSession
        {
            GymId = gymId,
            ClassId = classId,
            SessionDate = sessionDate,
            StartTime = startTime,
            DurationMinutes = durationMinutes,
            InstructorName = instructorName,
            Capacity = capacity,
        };
    }

    public void Cancel(string? reason)
    {
        Status = ClassSessionStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Trim().Length > 200 ? reason.Trim()[..200] : reason.Trim();
    }

    public bool IsCancelled => Status == ClassSessionStatus.Cancelled;
}
