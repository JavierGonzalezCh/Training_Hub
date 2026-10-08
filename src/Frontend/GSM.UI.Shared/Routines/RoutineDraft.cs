using GSM.Client.Generated;

namespace GSM.UI.Shared.Routines;

/// <summary>
/// Estado editable de una rutina, compartido por el editor del staff (ficha del miembro) y el
/// autoservicio del miembro (<c>/app/rutina/nueva</c>). Solo convierte hacia y desde los contratos.
/// </summary>
public sealed class RoutineDraft
{
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<DraftDay> Days { get; set; } = [];

    public static RoutineDraft Empty() => new() { Days = [new DraftDay()] };

    public static RoutineDraft From(RoutineResponse routine) => new()
    {
        Name = routine.Name,
        Notes = routine.Notes,
        Days = routine.Days.Select(d => new DraftDay
        {
            Id = d.Id,
            Name = d.Name,
            IsRest = d.IsRest,
            Exercises = d.Exercises.Select(e => new DraftExercise
            {
                Id = e.Id,
                Exercise = new ExerciseResponse { Id = e.ExerciseId, Name = e.ExerciseName },
                TargetSets = e.TargetSets,
                TargetReps = e.TargetReps,
                TargetWeightKg = e.TargetWeightKg is { } w ? (decimal)w : null,
                RestSeconds = e.RestSeconds,
                Notes = e.Notes,
            }).ToList(),
        }).ToList(),
    };

    /// <summary>Mensaje de error si el borrador no se puede enviar; <c>null</c> si está listo.</summary>
    public string? Validate() => string.IsNullOrWhiteSpace(Name) ? "El nombre es obligatorio." : null;

    /// <summary>Días de descanso sin ejercicios y filas sin ejercicio elegido se descartan.</summary>
    public SaveRoutineRequest ToRequest() => new()
    {
        Name = Name,
        Notes = Notes,
        Days = Days.Select(d => new RoutineDayInput
        {
            Id = d.Id,
            Name = string.IsNullOrWhiteSpace(d.Name) ? null : d.Name,
            IsRest = d.IsRest,
            Exercises = d.IsRest
                ? []
                : d.Exercises.Where(e => e.Exercise is not null).Select(e => new RoutineExerciseInput
                {
                    Id = e.Id,
                    ExerciseId = e.Exercise!.Id,
                    TargetSets = e.TargetSets,
                    TargetReps = e.TargetReps,
                    TargetWeightKg = e.TargetWeightKg is { } w ? (double)w : null,
                    RestSeconds = e.RestSeconds,
                    Notes = e.Notes,
                }).ToList(),
        }).ToList(),
    };
}

public sealed class DraftDay
{
    /// <summary>Id del día en el servidor al editar; <c>null</c> = día nuevo. Se reenvía para que la edición sea en el lugar.</summary>
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public bool IsRest { get; set; }
    public List<DraftExercise> Exercises { get; set; } = [];
}

public sealed class DraftExercise
{
    /// <summary>Id de la línea en el servidor al editar; <c>null</c> = línea nueva. Conservarlo mantiene su historial.</summary>
    public Guid? Id { get; set; }
    public ExerciseResponse? Exercise { get; set; }
    public int TargetSets { get; set; } = 3;
    public string TargetReps { get; set; } = "8-12";
    public decimal? TargetWeightKg { get; set; }
    public int? RestSeconds { get; set; } = 60;
    public string? Notes { get; set; }
}
