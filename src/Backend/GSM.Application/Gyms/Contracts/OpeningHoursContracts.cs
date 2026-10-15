namespace GSM.Application.Gyms.Contracts;

// Las horas viajan como texto "HH:mm" y los días como nombre ("Monday".."Sunday", "Holiday"): un enum
// suelto en el cuerpo de una respuesta sale mal en el cliente generado (ver CLAUDE.md, defectos de NSwag).

/// <summary>Un tramo de atención, "HH:mm" a "HH:mm".</summary>
public sealed record TimeSlotDto(string Opens, string Closes);

/// <summary>Los tramos de un día de la semana, o del horario de festivos (<c>Holiday</c>).</summary>
/// <remarks>
/// <c>Is24Hours</c> = "Abierto 24 horas": el día no lleva tramos y el gym nunca cierra. Es propiedad y no
/// parámetro posicional para que quien omite el campo (clientes viejos) lo deje en <c>false</c>.
/// </remarks>
public sealed record OpeningDayDto(string Day, IReadOnlyList<TimeSlotDto> Slots)
{
    public bool Is24Hours { get; init; }
}

/// <summary>Cómo está el gym ahora mismo. <c>Text</c> ya viene redactado ("Abierto · cierra a las 22:00").</summary>
public sealed record GymOpenStatusDto(
    bool HasSchedule,
    bool IsOpen,
    string? ClosesAt,
    DateOnly? NextOpensOn,
    string? NextOpensAt,
    string Text);

public sealed record OpeningHoursResponse(
    bool HasSchedule,
    IReadOnlyList<OpeningDayDto> Days,
    GymOpenStatusDto Status);

/// <summary>Reemplaza el horario completo: los 7 días y el de festivos, cada uno con al menos un tramo.</summary>
public sealed record UpdateOpeningHoursRequest(IReadOnlyList<OpeningDayDto> Days);

/// <summary>Un festivo del año con el horario que realmente aplica (el de festivo, o su ajuste).</summary>
public sealed record HolidayResponse(
    DateOnly Date,
    string Name,
    bool IsAdjusted,
    bool IsClosed,
    IReadOnlyList<TimeSlotDto> Slots,
    string? Note);

public sealed record SpecialDayResponse(
    DateOnly Date,
    bool IsClosed,
    IReadOnlyList<TimeSlotDto> Slots,
    string? Note,
    string? HolidayName);

/// <summary>
/// Otro horario en una fecha, o el día completo cerrado. Cerrar exige <c>ConfirmClosed = true</c>
/// ("¿Seguro? Ese día nadie podrá entrar y las clases se cancelan").
/// </summary>
public sealed record UpsertSpecialDayRequest(
    bool IsClosed,
    bool ConfirmClosed,
    IReadOnlyList<TimeSlotDto>? Slots,
    string? Note);

/// <summary>Horario del gym para la app del miembro: estado de ahora, semana y próximas excepciones.</summary>
public sealed record MemberGymHoursResponse(
    GymOpenStatusDto Status,
    IReadOnlyList<OpeningDayDto> Days,
    IReadOnlyList<SpecialDayResponse> Upcoming);
