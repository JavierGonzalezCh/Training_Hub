using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Un eslabón del ciclo rotativo. El descanso es una fila con <see cref="IsRest"/> = <c>true</c>,
/// no la ausencia de fila: es lo que distingue "hoy no le tocaba" de "hoy faltó".
/// </summary>
public sealed class RoutineDay : BaseEntity, ITenantEntity
{
    private readonly List<RoutineExercise> _exercises = [];

    private RoutineDay()
    {
    }

    public Guid GymId { get; private set; }

    public Guid RoutineId { get; private set; }

    /// <summary>1..N, posición en el ciclo.</summary>
    public int Position { get; private set; }

    public string? Name { get; private set; }

    public bool IsRest { get; private set; }

    /// <summary>Día de negocio en que el día salió de la rutina; no se borra, sus registros siguen ligados.</summary>
    public DateOnly? RetiredOn { get; private set; }

    /// <summary>Líneas vigentes (las retiradas no cuentan).</summary>
    public IReadOnlyList<RoutineExercise> Exercises => _exercises.Where(e => e.RetiredOn is null).ToList();

    internal static RoutineDay Create(Guid gymId, int position, RoutineDayDraft draft, DateOnly today)
    {
        var day = new RoutineDay { GymId = gymId };
        day.Apply(position, draft, today);
        return day;
    }

    internal void Update(int position, RoutineDayDraft draft, DateOnly today) => Apply(position, draft, today);

    internal void Retire(DateOnly on)
    {
        RetiredOn ??= on;
        foreach (var exercise in _exercises)
        {
            exercise.Retire(on);
        }
    }

    private void Apply(int position, RoutineDayDraft draft, DateOnly today)
    {
        Position = position;
        Name = string.IsNullOrWhiteSpace(draft.Name) ? null : draft.Name.Trim();
        IsRest = draft.IsRest;

        if (Name is { Length: > 60 })
        {
            throw new ValidationDomainException("El nombre del día no puede exceder 60 caracteres.");
        }

        if (draft.IsRest && draft.Exercises.Count > 0)
        {
            throw new ValidationDomainException("Un día de descanso no admite ejercicios.");
        }

        var current = _exercises.Where(e => e.RetiredOn is null).ToDictionary(e => e.Id);
        var kept = new HashSet<Guid>();
        var order = 1;
        foreach (var exerciseDraft in draft.Exercises)
        {
            if (exerciseDraft.Id is { } id)
            {
                if (!current.TryGetValue(id, out var existing))
                {
                    throw new ValidationDomainException(
                        "El body referencia una línea que no existe en este día de la rutina.");
                }

                if (!kept.Add(id))
                {
                    throw new ValidationDomainException("Una línea de la rutina viene repetida en el body.");
                }

                existing.Update(order++, exerciseDraft);
            }
            else
            {
                _exercises.Add(RoutineExercise.Create(GymId, order++, exerciseDraft));
            }
        }

        foreach (var removed in current.Values.Where(e => !kept.Contains(e.Id)))
        {
            removed.Retire(today);
        }
    }
}
