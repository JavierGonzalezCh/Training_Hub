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

    /// <summary><c>"rotating"</c> (ciclo Día 1…N) o <c>"weekly"</c> (cada día atado a un día de la semana).</summary>
    public string ScheduleMode { get; set; } = RotatingMode;

    /// <summary>Modo con el que llegó la rutina del servidor; <c>null</c> si es nueva. Cambiarlo crea una rutina nueva.</summary>
    public string? OriginalMode { get; private set; }

    public const string RotatingMode = "rotating";
    public const string WeeklyMode = "weekly";

    public bool IsWeekly => ScheduleMode == WeeklyMode;

    public bool ModeChanged => OriginalMode is not null && OriginalMode != ScheduleMode;

    /// <summary>
    /// Cambia el modo de calendario. A semanal: los días de descanso salen (el descanso es el día sin entrenamiento)
    /// y cada día recibe el primer día de la semana libre. A rotativo: se quitan los días de la semana.
    /// </summary>
    public void SetMode(string mode)
    {
        if (mode == ScheduleMode)
        {
            return;
        }

        ScheduleMode = mode;
        if (mode == WeeklyMode)
        {
            Days.RemoveAll(d => d.IsRest);
            if (Days.Count == 0)
            {
                Days.Add(new DraftDay());
            }

            foreach (var day in Days)
            {
                day.IsRest = false;
                day.DayOfWeek = null;
            }

            foreach (var day in Days)
            {
                day.DayOfWeek = FreeWeekdays(day).Cast<int?>().FirstOrDefault();
            }
        }
        else
        {
            foreach (var day in Days)
            {
                day.DayOfWeek = null;
            }
        }
    }

    /// <summary>Días de la semana (1..7) que aún no usa otro día; el del propio <paramref name="day"/> siempre cuenta.</summary>
    public IEnumerable<int> FreeWeekdays(DraftDay day)
        => Enumerable.Range(1, 7).Where(w => day.DayOfWeek == w || Days.All(d => d == day || d.DayOfWeek != w));

    /// <summary>Añade un día; en semanal toma el primer día de la semana libre.</summary>
    public DraftDay AddDay()
    {
        var day = new DraftDay();
        Days.Add(day);
        if (IsWeekly)
        {
            day.DayOfWeek = FreeWeekdays(day).Cast<int?>().FirstOrDefault();
        }

        return day;
    }

    public bool CanAddDay => !IsWeekly || Days.Count < 7;

    public static RoutineDraft Empty() => new() { Days = [new DraftDay()] };

    public static RoutineDraft From(RoutineResponse routine) => new()
    {
        Name = routine.Name,
        Notes = routine.Notes,
        ScheduleMode = routine.ScheduleMode == WeeklyMode ? WeeklyMode : RotatingMode,
        OriginalMode = routine.ScheduleMode == WeeklyMode ? WeeklyMode : RotatingMode,
        Days = routine.Days.Select(d => new DraftDay
        {
            Id = d.Id,
            Name = d.Name,
            IsRest = d.IsRest,
            DayOfWeek = d.DayOfWeek,
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
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "El nombre es obligatorio.";
        }

        if (!IsWeekly)
        {
            return null;
        }

        if (Days.Any(d => d.DayOfWeek is null))
        {
            return "Elige el día de la semana de cada día de la rutina.";
        }

        return Days.Select(d => d.DayOfWeek).Distinct().Count() != Days.Count
            ? "Un día de la semana no puede repetirse en la rutina."
            : null;
    }

    /// <summary>Días de descanso sin ejercicios y filas sin ejercicio elegido se descartan.</summary>
    public SaveRoutineRequest ToRequest() => new()
    {
        Name = Name,
        Notes = Notes,
        ScheduleMode = ScheduleMode,
        Days = Days.Select(d => new RoutineDayInput
        {
            Id = d.Id,
            DayOfWeek = IsWeekly ? d.DayOfWeek : null,
            Name = string.IsNullOrWhiteSpace(d.Name) ? null : d.Name,
            IsRest = !IsWeekly && d.IsRest,
            Exercises = !IsWeekly && d.IsRest
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

    /// <summary>Modo semanal: 1 = lunes … 7 = domingo.</summary>
    public int? DayOfWeek { get; set; }
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
