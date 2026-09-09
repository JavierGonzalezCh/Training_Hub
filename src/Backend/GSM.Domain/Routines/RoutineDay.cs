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

    public IReadOnlyList<RoutineExercise> Exercises => _exercises;

    internal static RoutineDay Create(Guid gymId, int position, RoutineDayDraft draft)
    {
        var day = new RoutineDay
        {
            GymId = gymId,
            Position = position,
            Name = string.IsNullOrWhiteSpace(draft.Name) ? null : draft.Name.Trim(),
            IsRest = draft.IsRest,
        };

        if (day.Name is { Length: > 60 })
        {
            throw new ValidationDomainException("El nombre del día no puede exceder 60 caracteres.");
        }

        if (draft.IsRest && draft.Exercises.Count > 0)
        {
            throw new ValidationDomainException("Un día de descanso no admite ejercicios.");
        }

        var order = 1;
        foreach (var exerciseDraft in draft.Exercises)
        {
            day._exercises.Add(RoutineExercise.Create(gymId, order++, exerciseDraft));
        }

        return day;
    }
}
