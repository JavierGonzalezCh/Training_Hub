using System.Globalization;
using GSM.Client.Generated;
using GSM.UI.Shared.Formatting;

namespace GSM.Panel.UI.Pages.Settings;

/// <summary>Un tramo en edición: las horas son texto "HH:mm" tal como las entrega el campo de hora.</summary>
public sealed class SlotDraft
{
    public string Opens { get; set; } = string.Empty;

    public string Closes { get; set; } = string.Empty;

    public SlotDraft()
    {
    }

    public SlotDraft(string opens, string closes)
    {
        Opens = opens;
        Closes = closes;
    }
}

/// <summary>Los tramos de un día (o de festivos) en edición.</summary>
public sealed class DayDraft(string day)
{
    public string Day { get; } = day;

    public string Label => OpeningHoursLabels.Label(Day);

    public List<SlotDraft> Slots { get; } = [];
}

/// <summary>
/// Horario semanal en edición y su validación en pantalla. Las reglas son las mismas que aplica el
/// servidor (al menos un tramo por día, abrir antes de cerrar, sin solaparse), para avisar antes de guardar.
/// </summary>
public sealed class OpeningHoursDraft
{
    public const int MaxSlotsPerDay = 4;

    public List<DayDraft> Days { get; } = OpeningHoursLabels.Days.Select(d => new DayDraft(d.Day)).ToList();

    /// <summary>Plantilla para empezar: L–V 5:00–22:00, sábado 7:00–14:00, domingo y festivos 7:00–13:00.</summary>
    public static OpeningHoursDraft Template()
    {
        var draft = new OpeningHoursDraft();
        foreach (var day in draft.Days)
        {
            day.Slots.Add(day.Day switch
            {
                "Saturday" => new SlotDraft("07:00", "14:00"),
                "Sunday" or "Holiday" => new SlotDraft("07:00", "13:00"),
                _ => new SlotDraft("05:00", "22:00"),
            });
        }

        return draft;
    }

    public static OpeningHoursDraft From(IEnumerable<OpeningDayDto> days)
    {
        var draft = new OpeningHoursDraft();
        foreach (var dto in days)
        {
            var day = draft.Days.FirstOrDefault(d => string.Equals(d.Day, dto.Day, StringComparison.OrdinalIgnoreCase));
            day?.Slots.AddRange(dto.Slots.Select(s => new SlotDraft(s.Opens, s.Closes)));
        }

        return draft;
    }

    /// <summary>Errores por día (clave: nombre del día). Un día sin error no aparece.</summary>
    public IReadOnlyDictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>();
        foreach (var day in Days)
        {
            if (ValidateSlots(day.Slots) is { } error)
            {
                errors[day.Day] = error;
            }
        }

        return errors;
    }

    public bool IsValid => Validate().Count == 0;

    public UpdateOpeningHoursRequest ToRequest() => new()
    {
        Days = Days.Select(d => new OpeningDayDto
        {
            Day = d.Day,
            Slots = d.Slots.Select(s => new TimeSlotDto { Opens = s.Opens, Closes = s.Closes }).ToList(),
        }).ToList(),
    };

    /// <summary>Valida los tramos de un día; <c>null</c> si están bien.</summary>
    public static string? ValidateSlots(IReadOnlyList<SlotDraft> slots)
    {
        if (slots.Count == 0)
        {
            return "Agrega al menos un tramo de atención.";
        }

        if (slots.Count > MaxSlotsPerDay)
        {
            return $"Máximo {MaxSlotsPerDay} tramos por día.";
        }

        var parsed = new List<(TimeOnly Opens, TimeOnly Closes)>();
        foreach (var slot in slots)
        {
            if (!TryTime(slot.Opens, out var opens) || !TryTime(slot.Closes, out var closes))
            {
                return "Completa la hora de apertura y de cierre de cada tramo.";
            }

            if (opens >= closes)
            {
                return "Cada tramo debe abrir antes de cerrar.";
            }

            parsed.Add((opens, closes));
        }

        var sorted = parsed.OrderBy(p => p.Opens).ToList();
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Opens < sorted[i - 1].Closes)
            {
                return "Los tramos no pueden solaparse.";
            }
        }

        return null;
    }

    private static bool TryTime(string? text, out TimeOnly time)
        => TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
