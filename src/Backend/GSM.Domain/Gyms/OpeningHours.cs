using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Gyms;

/// <summary>
/// Día al que pertenece un horario semanal. Los valores 0 a 6 coinciden con <see cref="DayOfWeek"/>;
/// <see cref="Holiday"/> es el horario que se aplica a los festivos de Colombia.
/// </summary>
public enum OpeningDay
{
    Sunday = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6,
    Holiday = 7,
}

/// <summary>Un tramo de apertura dentro de un mismo día: <c>[Opens, Closes)</c>. Cierra como máximo a las 23:59.</summary>
public readonly record struct TimeSlot(TimeOnly Opens, TimeOnly Closes)
{
    public bool Contains(TimeOnly time) => time >= Opens && time < Closes;
}

/// <summary>Reglas de los tramos de un día. Fail-fast: un tramo inválido es 422.</summary>
public static class OpeningSlots
{
    /// <summary>Máximo de tramos por día (un horario partido real no necesita más).</summary>
    public const int MaxSlotsPerDay = 4;

    /// <summary>
    /// Valida y ordena los tramos de un día: al menos uno, cada uno con apertura anterior al cierre, sin solaparse.
    /// </summary>
    public static IReadOnlyList<TimeSlot> Normalize(IEnumerable<TimeSlot> slots, string dayLabel)
    {
        var sorted = slots.OrderBy(s => s.Opens).ToList();
        if (sorted.Count == 0)
        {
            throw new ValidationDomainException($"{dayLabel}: indica al menos un tramo de atención.");
        }

        if (sorted.Count > MaxSlotsPerDay)
        {
            throw new ValidationDomainException($"{dayLabel}: máximo {MaxSlotsPerDay} tramos por día.");
        }

        for (var i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].Opens >= sorted[i].Closes)
            {
                throw new ValidationDomainException(
                    $"{dayLabel}: el tramo {sorted[i].Opens:HH\\:mm}–{sorted[i].Closes:HH\\:mm} debe abrir antes de cerrar.");
            }

            if (i > 0 && sorted[i].Opens < sorted[i - 1].Closes)
            {
                throw new ValidationDomainException($"{dayLabel}: los tramos no pueden solaparse.");
            }
        }

        return sorted;
    }
}

/// <summary>Un tramo del horario semanal de un gym (una fila por tramo, por día).</summary>
public sealed class GymOpeningSlot : BaseEntity, ITenantEntity
{
    private GymOpeningSlot()
    {
    }

    public Guid GymId { get; private set; }

    public OpeningDay Day { get; private set; }

    public TimeOnly OpensAt { get; private set; }

    public TimeOnly ClosesAt { get; private set; }

    public TimeSlot Slot => new(OpensAt, ClosesAt);

    public static GymOpeningSlot Create(Guid gymId, OpeningDay day, TimeSlot slot)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El gym es obligatorio.");
        }

        return new GymOpeningSlot { GymId = gymId, Day = day, OpensAt = slot.Opens, ClosesAt = slot.Closes };
    }
}

/// <summary>
/// Excepción de una fecha concreta: otro horario ese día, o el gym cerrado todo el día. También es la
/// forma de ajustar un festivo.
/// </summary>
public sealed class GymSpecialDay : BaseEntity, ITenantEntity
{
    public const int MaxNoteLength = 200;

    private readonly List<GymSpecialDaySlot> _slots = [];

    private GymSpecialDay()
    {
    }

    public Guid GymId { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Cerrado el día completo.</summary>
    public bool IsClosed { get; private set; }

    public string? Note { get; private set; }

    public IReadOnlyList<GymSpecialDaySlot> Slots => _slots;

    public IReadOnlyList<TimeSlot> TimeSlots
        => IsClosed ? [] : _slots.Select(s => s.Slot).OrderBy(s => s.Opens).ToList();

    public static GymSpecialDay Create(Guid gymId, DateOnly date, bool isClosed, IEnumerable<TimeSlot> slots, string? note)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El gym es obligatorio.");
        }

        var day = new GymSpecialDay { GymId = gymId, Date = date };
        day.Apply(isClosed, slots, note);
        return day;
    }

    public void Apply(bool isClosed, IEnumerable<TimeSlot> slots, string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxNoteLength })
        {
            throw new ValidationDomainException($"La nota admite hasta {MaxNoteLength} caracteres.");
        }

        IReadOnlyList<TimeSlot> normalized = [];
        if (!isClosed)
        {
            normalized = OpeningSlots.Normalize(slots, $"{Date:dd/MM/yyyy}");
        }

        IsClosed = isClosed;
        Note = trimmed;
        _slots.Clear();
        _slots.AddRange(normalized.Select(s => GymSpecialDaySlot.Create(GymId, Id, s)));
    }
}

/// <summary>Un tramo de la excepción de una fecha.</summary>
public sealed class GymSpecialDaySlot : BaseEntity, ITenantEntity
{
    private GymSpecialDaySlot()
    {
    }

    public Guid GymId { get; private set; }

    public Guid SpecialDayId { get; private set; }

    public TimeOnly OpensAt { get; private set; }

    public TimeOnly ClosesAt { get; private set; }

    public TimeSlot Slot => new(OpensAt, ClosesAt);

    internal static GymSpecialDaySlot Create(Guid gymId, Guid specialDayId, TimeSlot slot)
        => new() { GymId = gymId, SpecialDayId = specialDayId, OpensAt = slot.Opens, ClosesAt = slot.Closes };
}
