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

    /// <summary>C2: copia del entrenador de la definición al materializar (nulo = "Otro" o sin instructor).</summary>
    public Guid? InstructorUserId { get; private set; }

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
        int? capacity,
        Guid? instructorUserId = null)
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
            InstructorUserId = instructorUserId,
        };
    }

    /// <summary>
    /// C2: reasigna el instructor de una sesión futura cuando el staff cambia el de la clase. Las sesiones
    /// pasadas conservan lo que pasó; solo se llama para las que aún no ocurren.
    /// </summary>
    public void ReassignInstructor(Guid? instructorUserId, string? instructorName)
    {
        InstructorUserId = instructorUserId;
        InstructorName = instructorName;
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
