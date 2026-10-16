using GSM.Domain.Classes;

namespace GSM.Application.Classes.Contracts;

/// <summary>
/// Alta o edición de una definición de clase. <c>Capacity = null</c> significa entrada libre
/// (sin reserva). Para una clase recurrente, <c>DaysOfWeek</c> no vacío y <c>ScheduledDate</c> null;
/// para una puntual, al revés.
/// </summary>
public sealed record SaveClassRequest(
    string Name,
    string? Description,
    string? InstructorName,
    int? Capacity,
    TimeOnly StartTime,
    int DurationMinutes,
    bool IsRecurring,
    IReadOnlyList<ClassDayOfWeek>? DaysOfWeek,
    DateOnly? ScheduledDate,
    DateOnly? EndsOn = null,
    /// <summary>
    /// C2: el entrenador elegido de la lista del gym (<c>GET api/classes/trainers</c>). Con valor, el nombre del
    /// instructor se copia de su cuenta y <c>InstructorName</c> se ignora. Nulo = "Otro" (texto libre) o sin instructor.
    /// </summary>
    Guid? InstructorUserId = null,
    /// <summary>
    /// C6: las máquinas del inventario a las que se liga el cupo (ids de <c>GET api/equipment</c>). <c>null</c> en una edición
    /// = no tocar las ligas actuales; vacío = quitarlas todas. Exige cupo (una clase de entrada libre no se liga).
    /// </summary>
    IReadOnlyList<Guid>? EquipmentItemIds = null);

public sealed record ClassResponse(
    Guid Id,
    string Name,
    string? Description,
    string? InstructorName,
    int? Capacity,
    TimeOnly StartTime,
    int DurationMinutes,
    bool IsRecurring,
    IReadOnlyList<ClassDayOfWeek> DaysOfWeek,
    DateOnly? ScheduledDate,
    DateOnly StartsOn,
    bool IsActive,
    DateOnly? DeactivatedOn,
    DateOnly? EndsOn,
    Guid? InstructorUserId,
    bool NoActiveInstructor,
    /// <summary>C6: las máquinas ligadas al cupo (vacío = la clase usa solo su cupo).</summary>
    IReadOnlyList<Guid> EquipmentItemIds);

public sealed record ClassListItem(
    Guid Id,
    string Name,
    string? InstructorName,
    int? Capacity,
    TimeOnly StartTime,
    int DurationMinutes,
    bool IsRecurring,
    IReadOnlyList<ClassDayOfWeek> DaysOfWeek,
    DateOnly? ScheduledDate,
    bool IsActive,
    DateOnly? EndsOn,
    Guid? InstructorUserId,
    bool NoActiveInstructor);

/// <summary>Un entrenador elegible como instructor (Owner o staff con <c>Trainer</c>). "Otro" lo añade la UI.</summary>
public sealed record ClassInstructorOption(Guid UserId, string FullName, bool IsOwner);
