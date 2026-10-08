namespace GSM.UI.Shared.Formatting;

/// <summary>Catálogos cerrados de <c>Muscle</c>, <c>Equipment</c> y la clasificación de ejercicios (ver <c>GSM.Domain.Routines</c>) en español.</summary>
public static class RoutineLabels
{
    public static readonly (int Value, string Label)[] Muscles =
    [
        (0, "Pecho"), (1, "Espalda"), (2, "Hombros"), (3, "Bíceps"), (4, "Tríceps"),
        (5, "Antebrazos"), (6, "Cuádriceps"), (7, "Isquiotibiales"), (8, "Glúteos"),
        (9, "Pantorrillas"), (10, "Abdomen"), (11, "Lumbar"), (12, "Cuello"),
        (13, "Cuerpo completo"), (14, "Trapecio"), (15, "Oblicuos"), (16, "Aductores"), (17, "Abductores"),
    ];

    /// <summary>Zonas del cuerpo (enum <c>MuscleGroup</c>); se derivan del músculo principal.</summary>
    public static readonly (int Value, string Label)[] MuscleGroups =
    [
        (0, "Tren superior"), (1, "Tren inferior"), (2, "Core"), (3, "Cuerpo completo"),
    ];

    public static readonly (int Value, string Label)[] ExerciseTypes =
    [
        (0, "Fuerza"), (1, "Cardio"), (2, "Movilidad"), (3, "Estiramiento"), (4, "Pliometría"),
    ];

    public static readonly (int Value, string Label)[] TrackingTypes =
    [
        (0, "Repeticiones y peso"), (1, "Repeticiones"), (2, "Tiempo"), (3, "Distancia"), (4, "Tiempo y distancia"),
    ];

    public static readonly (int Value, string Label)[] Difficulties =
    [
        (0, "Principiante"), (1, "Intermedio"), (2, "Avanzado"),
    ];

    public static readonly (int Value, string Label)[] Equipment =
    [
        (0, "Barra"), (1, "Mancuerna"), (2, "Kettlebell"), (3, "Máquina"), (4, "Polea"),
        (5, "Máquina Smith"), (6, "Banco"), (7, "Rack"), (8, "Banda elástica"), (9, "TRX"),
        (10, "Balón medicinal"), (11, "Colchoneta"), (12, "Cajón"), (13, "Cuerda de saltar"),
        (14, "Peso corporal"), (15, "Máquina de cardio"),
    ];

    /// <summary>
    /// Espejo de <c>MuscleGroups.Of</c> del dominio (ordinal de <c>Muscle</c> → ordinal de <c>MuscleGroup</c>).
    /// El filtro del catálogo es en dos pasos: primero la zona, después solo sus músculos.
    /// </summary>
    private static readonly Dictionary<int, int> GroupOfMuscle = new()
    {
        [0] = 0, [1] = 0, [2] = 0, [3] = 0, [4] = 0, [5] = 0, [12] = 0, [14] = 0,
        [6] = 1, [7] = 1, [8] = 1, [9] = 1, [16] = 1, [17] = 1,
        [10] = 2, [11] = 2, [15] = 2,
        [13] = 3,
    };

    /// <summary>Los músculos (valor, etiqueta) de una zona del cuerpo.</summary>
    public static IEnumerable<(int Value, string Label)> MusclesOfGroup(int group)
        => Muscles.Where(m => GroupOfMuscle.GetValueOrDefault(m.Value, -1) == group);

    /// <summary>Qué se mide por defecto según el tipo de ejercicio (espejo de <c>ExerciseDefaults.SuggestedTracking</c>).</summary>
    public static int SuggestedTracking(int exerciseType) => exerciseType switch
    {
        1 => 4,         // cardio → tiempo y distancia
        2 or 3 => 2,    // movilidad, estiramiento → tiempo
        _ => 0,         // el resto → repeticiones y peso
    };

    private static readonly Dictionary<string, string> MuscleByName = new()
    {
        ["Chest"] = "Pecho", ["Back"] = "Espalda", ["Shoulders"] = "Hombros", ["Biceps"] = "Bíceps",
        ["Triceps"] = "Tríceps", ["Forearms"] = "Antebrazos", ["Quadriceps"] = "Cuádriceps",
        ["Hamstrings"] = "Isquiotibiales", ["Glutes"] = "Glúteos", ["Calves"] = "Pantorrillas",
        ["Abs"] = "Abdomen", ["Lower_back"] = "Lumbar", ["Neck"] = "Cuello",
        ["Full_body"] = "Cuerpo completo", ["Traps"] = "Trapecio", ["Obliques"] = "Oblicuos",
        ["Adductors"] = "Aductores", ["Abductors"] = "Abductores",
    };

    private static readonly Dictionary<string, string> GroupByName = new()
    {
        ["UpperBody"] = "Tren superior", ["LowerBody"] = "Tren inferior", ["Core"] = "Core", ["FullBody"] = "Cuerpo completo",
    };

    private static readonly Dictionary<string, string> ExerciseTypeByName = new()
    {
        ["Strength"] = "Fuerza", ["Cardio"] = "Cardio", ["Mobility"] = "Movilidad",
        ["Stretching"] = "Estiramiento", ["Plyometrics"] = "Pliometría",
    };

    private static readonly Dictionary<string, string> TrackingByName = new()
    {
        ["RepsWeight"] = "Repeticiones y peso", ["Reps"] = "Repeticiones", ["Time"] = "Tiempo",
        ["Distance"] = "Distancia", ["TimeDistance"] = "Tiempo y distancia",
    };

    private static readonly Dictionary<string, string> DifficultyByName = new()
    {
        ["Beginner"] = "Principiante", ["Intermediate"] = "Intermedio", ["Advanced"] = "Avanzado",
    };

    private static readonly Dictionary<string, string> EquipmentByName = new()
    {
        ["Barbell"] = "Barra", ["Dumbbell"] = "Mancuerna", ["Kettlebell"] = "Kettlebell",
        ["Machine"] = "Máquina", ["Cable"] = "Polea", ["Smith_machine"] = "Máquina Smith",
        ["Bench"] = "Banco", ["Rack"] = "Rack", ["Resistance_band"] = "Banda elástica",
        ["Trx"] = "TRX", ["Medicine_ball"] = "Balón medicinal", ["Mat"] = "Colchoneta",
        ["Box"] = "Cajón", ["Jump_rope"] = "Cuerda de saltar", ["Bodyweight"] = "Peso corporal",
        ["Cardio_machine"] = "Máquina de cardio",
    };

    public static string MuscleLabel(string name) => MuscleByName.GetValueOrDefault(name, name);

    public static string MuscleGroupLabel(string name) => GroupByName.GetValueOrDefault(name, name);

    public static string ExerciseTypeLabel(string name) => ExerciseTypeByName.GetValueOrDefault(name, name);

    public static string TrackingTypeLabel(string name) => TrackingByName.GetValueOrDefault(name, name);

    public static string DifficultyLabel(string name) => DifficultyByName.GetValueOrDefault(name, name);

    public static string EquipmentLabel(string name) => EquipmentByName.GetValueOrDefault(name, name);

    /// <summary>"Tren superior · Core": las zonas que trabaja un día, o <c>null</c> si no hay ninguna.</summary>
    public static string? MuscleGroupsSummary(IEnumerable<string>? groups)
    {
        var labels = (groups ?? []).Select(MuscleGroupLabel).ToList();
        return labels.Count == 0 ? null : string.Join(" · ", labels);
    }

    /// <summary>Ordinal de un valor de enum del catálogo a partir del nombre que viaja en las respuestas.</summary>
    public static int MuscleValue(string name)
        => Muscles.FirstOrDefault(m => m.Label == MuscleLabel(name)).Value;

    public static int ExerciseTypeValue(string name)
        => ExerciseTypes.FirstOrDefault(t => t.Label == ExerciseTypeLabel(name)).Value;

    public static int TrackingTypeValue(string name)
        => TrackingTypes.FirstOrDefault(t => t.Label == TrackingTypeLabel(name)).Value;

    public static int DifficultyValue(string name)
        => Difficulties.FirstOrDefault(t => t.Label == DifficultyLabel(name)).Value;

    /// <summary>Días de la semana ISO de las rutinas semanales: 1 = lunes … 7 = domingo.</summary>
    public static readonly (int Value, string Label)[] IsoWeekdays =
    [
        (1, "Lunes"), (2, "Martes"), (3, "Miércoles"), (4, "Jueves"), (5, "Viernes"), (6, "Sábado"), (7, "Domingo"),
    ];

    public static string IsoWeekdayLabel(int value)
        => value is >= 1 and <= 7 ? IsoWeekdays[value - 1].Label : value.ToString();

    public static string ScheduleModeLabel(string? mode) => mode == "weekly" ? "Semanal" : "Rotativa";
}
