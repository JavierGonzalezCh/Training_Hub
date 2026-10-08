using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Rutina de un socio: una cabecera más un ciclo rotativo de días. El día 1 del ciclo cae en
/// <see cref="StartsOn"/> y los <c>N</c> días se repiten sin atarse a la semana.
/// </summary>
public sealed class Routine : BaseEntity, ITenantEntity
{
    private readonly List<RoutineDay> _days = [];

    private Routine()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    /// <summary>Siempre de <c>ICurrentUserService</c>, nunca del body.</summary>
    public Guid AssignedBy { get; private set; }

    /// <summary>La creó el propio socio. Las rutinas con <c>true</c> no puntúan para logros.</summary>
    public bool SelfAssigned { get; private set; }

    /// <summary>Ancla del ciclo (día de negocio, de <c>IClock.Today</c>).</summary>
    public DateOnly StartsOn { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateOnly? DeactivatedOn { get; private set; }

    /// <summary>Días vigentes (los retirados no cuentan).</summary>
    public IReadOnlyList<RoutineDay> Days => _days.Where(d => d.RetiredOn is null).ToList();

    /// <summary>Longitud del ciclo = número de días.</summary>
    public int CycleLength => _days.Count(d => d.RetiredOn is null);

    public static Routine Assign(
        Guid gymId, Guid memberId, DateOnly startsOn, RoutineDraft draft)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("La rutina necesita gym y socio.");
        }

        var routine = new Routine
        {
            GymId = gymId,
            MemberId = memberId,
            AssignedBy = draft.AssignedBy,
            SelfAssigned = draft.SelfAssigned,
            StartsOn = startsOn,
        };

        routine.ApplyHeaderAndDays(draft, startsOn);
        return routine;
    }

    /// <summary>
    /// Edita en el lugar: lo que trae id se actualiza y conserva su id (y sus registros), lo que no trae
    /// id se crea y lo que ya no viene se retira con <paramref name="today"/> sin borrarse. No cambia
    /// <see cref="StartsOn"/>: es el ancla histórica del ciclo.
    /// </summary>
    public void Replace(RoutineDraft draft, DateOnly today) => ApplyHeaderAndDays(draft, today);

    public void Deactivate(DateOnly on)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DeactivatedOn = on;
    }

    private void ApplyHeaderAndDays(RoutineDraft draft, DateOnly today)
    {
        var name = NormalizeName(draft.Name);

        if (draft.Days.Count < 1)
        {
            throw new ValidationDomainException("La rutina necesita al menos un día.");
        }

        Name = name;
        Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim();

        var current = _days.Where(d => d.RetiredOn is null).ToDictionary(d => d.Id);
        var kept = new HashSet<Guid>();
        var position = 1;
        foreach (var dayDraft in draft.Days)
        {
            if (dayDraft.Id is { } id)
            {
                if (!current.TryGetValue(id, out var existing))
                {
                    throw new ValidationDomainException("El body referencia un día que no existe en esta rutina.");
                }

                if (!kept.Add(id))
                {
                    throw new ValidationDomainException("Un día de la rutina viene repetido en el body.");
                }

                existing.Update(position++, dayDraft, today);
            }
            else
            {
                _days.Add(RoutineDay.Create(GymId, position++, dayDraft, today));
            }
        }

        foreach (var removed in current.Values.Where(d => !kept.Contains(d.Id)))
        {
            removed.Retire(today);
        }
    }

    private static string NormalizeName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is < 2 or > 120)
        {
            throw new ValidationDomainException("El nombre de la rutina debe tener entre 2 y 120 caracteres.");
        }

        return value;
    }
}
