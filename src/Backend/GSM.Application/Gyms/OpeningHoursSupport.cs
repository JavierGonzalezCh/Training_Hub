using System.Globalization;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;

namespace GSM.Application.Gyms;

/// <summary>Conversión entre el horario del dominio y el de los contratos, y armado de <see cref="OpeningSchedule"/>.</summary>
public static class OpeningHoursSupport
{
    public const string TimeFormat = "HH:mm";

    public static readonly OpeningDay[] AllDays =
    [
        OpeningDay.Monday, OpeningDay.Tuesday, OpeningDay.Wednesday, OpeningDay.Thursday,
        OpeningDay.Friday, OpeningDay.Saturday, OpeningDay.Sunday, OpeningDay.Holiday,
    ];

    public static string Format(TimeOnly time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out TimeOnly time)
        => TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static bool IsValidTime(string? text) => TryParse(text, out _);

    public static TimeSlotDto ToDto(TimeSlot slot) => new(Format(slot.Opens), Format(slot.Closes));

    public static IReadOnlyList<TimeSlotDto> ToDto(IEnumerable<TimeSlot> slots)
        => slots.OrderBy(s => s.Opens).Select(ToDto).ToList();

    public static IReadOnlyList<TimeSlot> ToSlots(IEnumerable<TimeSlotDto>? dtos)
        => (dtos ?? []).Select(d =>
        {
            if (!TryParse(d.Opens, out var opens) || !TryParse(d.Closes, out var closes))
            {
                throw new ValidationDomainException("Las horas deben tener el formato HH:mm.");
            }

            return new TimeSlot(opens, closes);
        }).ToList();

    public static OpeningSchedule BuildSchedule(
        IEnumerable<GymOpeningSlot> weeklySlots, IEnumerable<GymSpecialDay> specialDays)
    {
        var weekly = weeklySlots
            .GroupBy(s => s.Day)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TimeSlot>)g.Select(s => s.Slot).OrderBy(s => s.Opens).ToList());
        var special = specialDays.ToDictionary(d => d.Date, d => new SpecialDayData(d.IsClosed, d.TimeSlots));
        return new OpeningSchedule(weekly, special);
    }

    public static IReadOnlyList<OpeningDayDto> ToDays(IEnumerable<GymOpeningSlot> weeklySlots)
    {
        var byDay = weeklySlots.GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.Select(s => s.Slot));
        return AllDays
            .Where(byDay.ContainsKey)
            .Select(d => new OpeningDayDto(d.ToString(), ToDto(byDay[d])))
            .ToList();
    }

    public static GymOpenStatusDto ToDto(GymOpenStatus status, DateOnly today)
        => new(
            status.HasSchedule,
            status.IsOpen,
            status.ClosesAt is { } closes ? Format(closes) : null,
            status.NextOpensOn,
            status.NextOpensAt is { } opens ? Format(opens) : null,
            OpenStatusText.Badge(status, today));

    public static SpecialDayResponse ToResponse(GymSpecialDay day)
        => new(day.Date, day.IsClosed, ToDto(day.TimeSlots), day.Note, ColombianHolidays.Find(day.Date)?.Name);
}

/// <summary>Textos del estado del gym, redactados en el servidor para que todas las pantallas digan lo mismo.</summary>
public static class OpenStatusText
{
    private static readonly string[] Weekdays =
        ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

    private static readonly string[] Months =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    /// <summary>"5:00", "22:00": la hora sin cero a la izquierda.</summary>
    public static string Hour(TimeOnly time) => $"{time.Hour}:{time.Minute:00}";

    public static string When(DateOnly date, DateOnly today)
    {
        var diff = date.DayNumber - today.DayNumber;
        return diff switch
        {
            0 => "hoy",
            1 => "mañana",
            < 7 => $"el {Weekdays[(int)date.DayOfWeek]}",
            _ => $"el {Weekdays[(int)date.DayOfWeek]} {date.Day} de {Months[date.Month - 1]}",
        };
    }

    /// <summary>"Abierto · cierra a las 22:00" o "Cerrado · abre mañana a las 5:00".</summary>
    public static string Badge(GymOpenStatus status, DateOnly today)
    {
        if (!status.HasSchedule)
        {
            return "Horario sin cargar";
        }

        if (status.IsOpen)
        {
            return status.ClosesAt is { } closes ? $"Abierto · cierra a las {Hour(closes)}" : "Abierto";
        }

        return status.NextOpensOn is { } on && status.NextOpensAt is { } at
            ? $"Cerrado · abre {When(on, today)} a las {Hour(at)}"
            : "Cerrado";
    }

    /// <summary>El motivo del rechazo: "El gimnasio está cerrado. Abre mañana a las 5:00".</summary>
    public static string ClosedMessage(GymOpenStatus status, DateOnly today)
        => status.NextOpensOn is { } on && status.NextOpensAt is { } at
            ? $"El gimnasio está cerrado. Abre {When(on, today)} a las {Hour(at)}"
            : "El gimnasio está cerrado.";
}
