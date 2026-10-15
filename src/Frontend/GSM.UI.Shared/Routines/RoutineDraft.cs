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
                Exercise = new ExerciseResponse
                {
                    Id = e.ExerciseId,
                    Name = e.ExerciseName,
                    MuscleGroup = e.MuscleGroup ?? "FullBody",
                    TrackingType = e.TrackingType ?? "RepsWeight",
                    IsUnilateral = e.IsUnilateral ?? false,
                },
                Sets = (e.SetTargets ?? []).OrderBy(t => t.Number).Select(t => new DraftSet
                {
                    SetType = (t.SetType ?? "normal").ToLowerInvariant(),
                    RepsMin = t.RepsMin,
                    RepsMax = t.RepsMax,
                    WeightKg = t.WeightKg is { } w ? (decimal)w : null,
                    DurationSeconds = t.DurationSeconds,
                    DistanceM = t.DistanceM,
                    Rir = t.Rir,
                }).ToList(),
                Group = e.Group,
                RestSeconds = e.RestSeconds,
                Notes = e.Notes,
                Equipment = e.Equipment,
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
                : d.RequestLines().Select(x => new RoutineExerciseInput
                {
                    Id = x.Line.Id,
                    ExerciseId = x.Line.Exercise!.Id,
                    SetTargets = x.Line.Sets.Select(t => new SetTargetInput
                    {
                        SetType = t.SetType,
                        RepsMin = t.RepsMin,
                        RepsMax = t.RepsMax,
                        WeightKg = t.WeightKg is { } w ? (double)w : null,
                        DurationSeconds = t.DurationSeconds,
                        DistanceM = t.DistanceM,
                        Rir = t.Rir,
                    }).ToList(),
                    Group = x.Group,
                    RestSeconds = x.Line.RestSeconds,
                    Notes = x.Line.Notes,
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

    /// <summary>Zonas del cuerpo que trabaja el día (<c>UpperBody</c>…), sin repetir y en orden de aparición.</summary>
    public IReadOnlyList<string> MuscleGroups
        => Exercises
            .Where(e => !string.IsNullOrEmpty(e.Exercise?.MuscleGroup))
            .Select(e => e.Exercise!.MuscleGroup)
            .Distinct()
            .ToList();

    /// <summary>"Tren superior · Core": nombre sugerido cuando el día no tiene nombre; <c>null</c> sin ejercicios.</summary>
    public string? SuggestedName => GSM.UI.Shared.Formatting.RoutineLabels.MuscleGroupsSummary(MuscleGroups);

    /// <summary>¿La línea está unida a la anterior en una superserie o circuito?</summary>
    public bool IsLinkedWithPrevious(DraftExercise line)
    {
        var index = Exercises.IndexOf(line);
        return index > 0 && line.Group is not null && Exercises[index - 1].Group == line.Group;
    }

    /// <summary>Une la línea con la anterior (misma superserie) o, si ya estaba unida, la separa junto con las que le siguen.</summary>
    public void ToggleLinkWithPrevious(DraftExercise line)
    {
        var index = Exercises.IndexOf(line);
        if (index <= 0)
        {
            return;
        }

        var previous = Exercises[index - 1];
        var next = Exercises.Max(e => e.Group ?? 0) + 1;
        if (IsLinkedWithPrevious(line))
        {
            // La línea y las que le siguen del mismo grupo pasan a un grupo propio; si queda una sola, se suelta al enviar.
            var old = line.Group;
            for (var i = index; i < Exercises.Count && Exercises[i].Group == old; i++)
            {
                Exercises[i].Group = next;
            }
        }
        else
        {
            previous.Group ??= next;
            line.Group = previous.Group;
        }
    }

    /// <summary>
    /// Las líneas que se envían (con ejercicio elegido) y el grupo de cada una: las rachas contiguas con el mismo número
    /// y de dos o más líneas forman un grupo numerado 1..n; el resto va suelto. Un borrador nunca envía un grupo partido
    /// ni de una sola línea.
    /// </summary>
    public IReadOnlyList<(DraftExercise Line, int? Group)> RequestLines()
    {
        var lines = Exercises.Where(e => e.Exercise is not null).ToList();
        var result = new List<(DraftExercise, int?)>();
        var next = 1;
        var i = 0;
        while (i < lines.Count)
        {
            var run = 1;
            if (lines[i].Group is { } group)
            {
                while (i + run < lines.Count && lines[i + run].Group == group)
                {
                    run++;
                }
            }

            int? assigned = run > 1 ? next++ : null;
            for (var k = 0; k < run; k++)
            {
                result.Add((lines[i + k], assigned));
            }

            i += run;
        }

        return result;
    }
}

public sealed class DraftExercise
{
    /// <summary>Id de la línea en el servidor al editar; <c>null</c> = línea nueva. Conservarlo mantiene su historial.</summary>
    public Guid? Id { get; set; }
    public ExerciseResponse? Exercise { get; set; }

    /// <summary>Series planeadas, una por una. Una línea nueva arranca con 3 series de 8–12 repeticiones.</summary>
    public List<DraftSet> Sets { get; set; } = DraftSet.DefaultsFor(null);

    /// <summary>
    /// Superserie o circuito: las líneas contiguas con el mismo número se hacen seguidas. <c>null</c> = suelto.
    /// Al enviar, los números se renumeran y los grupos de una sola línea se sueltan (ver <see cref="DraftDay.RequestLines"/>).
    /// </summary>
    public int? Group { get; set; }

    public int? RestSeconds { get; set; } = 60;
    public string? Notes { get; set; }

    /// <summary>
    /// Disponibilidad del ejercicio en el gym y sus máquinas (Equipment §5). Viene de la rutina guardada o del
    /// ejercicio elegido; solo alimenta el aviso del editor, nunca se envía al servidor.
    /// </summary>
    public ExerciseEquipmentInfo? Equipment { get; set; }

    /// <summary>Aviso del editor ("Prensa 45° en mantenimiento hasta el 15 oct"); no impide guardar.</summary>
    public string? EquipmentWarning => EquipmentNotice.Warning(Equipment);

    /// <summary>Qué campos de cada serie aplican al ejercicio elegido.</summary>
    public TrackingFields Fields => TrackingFields.Of(Exercise?.TrackingType);

    /// <summary>Elige el ejercicio; si cambia lo que se mide, las series se reemplazan por unas de partida adecuadas.</summary>
    public void Select(ExerciseResponse? exercise)
    {
        var before = Exercise?.TrackingType;

        // Otro ejercicio no es la misma línea: la anterior se retira (su historial queda con ella) y esta es nueva.
        if (Exercise is not null && exercise?.Id != Exercise.Id)
        {
            Id = null;
        }

        Exercise = exercise;
        Equipment = exercise is null
            ? null
            : new ExerciseEquipmentInfo
            {
                IsAvailable = exercise.IsAvailableHere != false,
                Machines = exercise.Machines?.ToList() ?? [],
            };
        if (exercise is not null && exercise.TrackingType != before)
        {
            Sets = DraftSet.DefaultsFor(exercise.TrackingType);
        }
    }

    /// <summary>
    /// "Cambiar por…" (R5): pone la alternativa en el lugar del ejercicio, conservando descanso, notas y superserie. Las
    /// series se conservan si el ejercicio nuevo mide lo mismo; si no, arrancan de partida (ver <see cref="Select"/>).
    /// </summary>
    public void ReplaceWith(ExerciseAlternativeResponse alternative)
        => Select(new ExerciseResponse
        {
            Id = alternative.Id,
            Name = alternative.Name,
            PrimaryMuscle = alternative.PrimaryMuscle,
            MuscleGroup = alternative.MuscleGroup,
            ExerciseType = alternative.ExerciseType,
            TrackingType = alternative.TrackingType,
            IsUnilateral = alternative.IsUnilateral,
            Difficulty = alternative.Difficulty,
            ImageUrl = alternative.ImageUrl,
            IsAvailableHere = alternative.IsAvailableHere,
            Machines = alternative.Machines,
        });

    /// <summary>Añade una serie copiando la última (o una de partida si no hay).</summary>
    public DraftSet AddSet()
    {
        var set = Sets.Count > 0 ? Sets[^1].Clone() : DraftSet.DefaultsFor(Exercise?.TrackingType)[0];
        Sets.Add(set);
        return set;
    }
}

/// <summary>Objetivo de una serie en el editor. Los campos que no aplican al ejercicio se ignoran en el servidor.</summary>
public sealed class DraftSet
{
    /// <summary><c>normal</c>, <c>warmup</c>, <c>drop</c> o <c>failure</c>.</summary>
    public string SetType { get; set; } = "normal";
    public int? RepsMin { get; set; }
    public int? RepsMax { get; set; }
    public decimal? WeightKg { get; set; }
    public int? DurationSeconds { get; set; }
    public int? DistanceM { get; set; }
    public int? Rir { get; set; }

    public bool IsFailure => SetType == "failure";

    public DraftSet Clone() => (DraftSet)MemberwiseClone();

    /// <summary>Series de partida según lo que se mide: tres de 8–12 repeticiones, o una de tiempo/distancia.</summary>
    public static List<DraftSet> DefaultsFor(string? tracking)
    {
        var fields = TrackingFields.Of(tracking);
        return Enumerable.Range(0, fields.Reps || fields.Weight ? 3 : 1).Select(_ => new DraftSet
        {
            RepsMin = fields.Reps ? 8 : null,
            RepsMax = fields.Reps ? 12 : null,
            DurationSeconds = fields.Time ? (fields.Distance ? 600 : 30) : null,
            DistanceM = fields.Distance ? (fields.Weight ? 40 : 1000) : null,
        }).ToList();
    }
}
