using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Rutina de un miembro: una cabecera más un ciclo rotativo de días. El día 1 del ciclo cae en
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

    /// <summary>La creó el propio miembro. Las rutinas con <c>true</c> no puntúan para logros.</summary>
    public bool SelfAssigned { get; private set; }

    /// <summary>Ancla del ciclo (día de negocio, de <c>IClock.Today</c>).</summary>
    public DateOnly StartsOn { get; private set; }

    /// <summary>Se elige al crear; cambiarlo es crear otra rutina.</summary>
    public ScheduleMode ScheduleMode { get; private set; } = ScheduleMode.Rotating;

    public bool IsActive { get; private set; } = true;

    public DateOnly? DeactivatedOn { get; private set; }

    /// <summary>
    /// Fecha de revisión: <c>starts_on</c> más las semanas del gym. Es un recordatorio para el entrenador, no un
    /// vencimiento: la rutina sigue activa aunque la fecha pase.
    /// </summary>
    public DateOnly ReviewOn { get; private set; }

    public DateTimeOffset? LastReviewedAt { get; private set; }

    public Guid? LastReviewedBy { get; private set; }

    /// <summary>Días vigentes (los retirados no cuentan).</summary>
    public IReadOnlyList<RoutineDay> Days => _days.Where(d => d.RetiredOn is null).ToList();

    /// <summary>Longitud del ciclo = número de días.</summary>
    public int CycleLength => _days.Count(d => d.RetiredOn is null);

    public static Routine Assign(
        Guid gymId, Guid memberId, DateOnly startsOn, RoutineDraft draft,
        int reviewWeeks = RoutineSettings.DefaultReviewWeeks)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("La rutina necesita gym y miembro.");
        }

        var routine = new Routine
        {
            GymId = gymId,
            MemberId = memberId,
            AssignedBy = draft.AssignedBy,
            SelfAssigned = draft.SelfAssigned,
            StartsOn = startsOn,
            ScheduleMode = draft.ScheduleMode,
            ReviewOn = startsOn.AddDays(7 * ValidReviewWeeks(reviewWeeks)),
        };

        routine.ApplyHeaderAndDays(draft, startsOn);
        return routine;
    }

    /// <summary>
    /// Edita en el lugar: lo que trae id se actualiza y conserva su id (y sus registros), lo que no trae
    /// id se crea y lo que ya no viene se retira con <paramref name="today"/> sin borrarse. No cambia
    /// <see cref="StartsOn"/>: es el ancla histórica del ciclo.
    /// </summary>
    public void Replace(RoutineDraft draft, DateOnly today)
    {
        if (draft.ScheduleMode != ScheduleMode)
        {
            throw new ValidationDomainException(
                "El modo de calendario no se edita: cambiarlo crea una rutina nueva.");
        }

        ApplyHeaderAndDays(draft, today);
    }

    /// <summary>
    /// El entrenador revisó la rutina: queda registrado quién y cuándo, y la próxima revisión corre
    /// <paramref name="reviewWeeks"/> semanas a partir de <paramref name="today"/> (no de la fecha anterior: una
    /// revisión tardía no puede dejar la siguiente ya vencida). Solo se revisa una rutina activa.
    /// </summary>
    public void MarkReviewed(Guid reviewedBy, DateTimeOffset now, DateOnly today, int reviewWeeks)
    {
        if (!IsActive)
        {
            throw new ConflictException("La rutina está dada de baja: no se puede marcar como revisada.");
        }

        if (reviewedBy == Guid.Empty)
        {
            throw new ValidationDomainException("Falta quién revisó la rutina.");
        }

        LastReviewedAt = now;
        LastReviewedBy = reviewedBy;
        ReviewOn = today.AddDays(7 * ValidReviewWeeks(reviewWeeks));
    }

    private static int ValidReviewWeeks(int weeks)
        => weeks is < RoutineSettings.MinReviewWeeks or > RoutineSettings.MaxReviewWeeks
            ? throw new ValidationDomainException("Las semanas de revisión no son válidas.")
            : weeks;

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

        var days = ValidateSchedule(draft);

        Name = name;
        Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim();

        var current = _days.Where(d => d.RetiredOn is null).ToDictionary(d => d.Id);
        var kept = new HashSet<Guid>();
        var position = 1;
        foreach (var dayDraft in days)
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

    /// <summary>
    /// Modo semanal: cada día lleva un <c>day_of_week</c> 1..7 sin repetir y no hay filas de descanso (el día sin
    /// fila lo es); los días quedan ordenados de lunes a domingo. Rotativo: sin <c>day_of_week</c>.
    /// </summary>
    private IReadOnlyList<RoutineDayDraft> ValidateSchedule(RoutineDraft draft)
    {
        if (ScheduleMode == ScheduleMode.Rotating)
        {
            if (draft.Days.Any(d => d.DayOfWeek is not null))
            {
                throw new ValidationDomainException(
                    "Una rutina rotativa no ata sus días a la semana: quita el día de la semana.");
            }

            return draft.Days;
        }

        if (draft.Days.Any(d => d.DayOfWeek is null))
        {
            throw new ValidationDomainException("En una rutina semanal cada día necesita su día de la semana.");
        }

        if (draft.Days.Any(d => d.DayOfWeek is < IsoWeekDay.Monday or > IsoWeekDay.Sunday))
        {
            throw new ValidationDomainException("El día de la semana debe ir de 1 (lunes) a 7 (domingo).");
        }

        if (draft.Days.Select(d => d.DayOfWeek).Distinct().Count() != draft.Days.Count)
        {
            throw new ValidationDomainException("Un día de la semana no puede repetirse en la rutina.");
        }

        if (draft.Days.Any(d => d.IsRest))
        {
            throw new ValidationDomainException(
                "En una rutina semanal el descanso es el día sin entrenamiento: no lleva fila propia.");
        }

        return draft.Days.OrderBy(d => d.DayOfWeek).ToList();
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
