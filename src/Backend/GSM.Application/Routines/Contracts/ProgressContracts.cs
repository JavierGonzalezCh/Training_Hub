namespace GSM.Application.Routines.Contracts;

/// <summary>
/// Un récord personal de un ejercicio. <c>Kind</c>: <c>best_weight</c>, <c>best_set_volume</c> (peso × repeticiones de la
/// mejor serie), <c>best_session_volume</c> (Σ peso × repeticiones de un día), <c>best_reps</c>, <c>best_session_reps</c>,
/// <c>best_duration</c> o <c>best_distance</c>. <c>Unit</c>: <c>kg</c>, <c>reps</c>, <c>s</c> o <c>m</c>. Los
/// calentamientos no cuentan. Los kinds que salen dependen del <c>tracking_type</c> del ejercicio.
/// </summary>
public sealed record PersonalRecordItem(string Kind, decimal Value, string Unit, DateOnly AchievedOn);

/// <summary>
/// Lo mejor de un ejercicio en un día (todas las líneas que lo registraron ese día juntas, sin calentamientos).
/// Cada campo es nulo si el ejercicio no mide eso. <c>TotalVolume</c> = Σ peso × repeticiones.
/// </summary>
public sealed record ProgressPoint(
    DateOnly PerformedOn,
    int SetCount,
    decimal? BestWeightKg,
    decimal? BestSetVolume,
    decimal? TotalVolume,
    int? BestReps,
    int? TotalReps,
    int? BestDurationSeconds,
    int? TotalDurationSeconds,
    int? BestDistanceM,
    int? TotalDistanceM);

/// <summary>Un ejercicio que el miembro ya ha registrado, con sus récords.</summary>
public sealed record ExerciseProgressSummary(
    Guid ExerciseId,
    string ExerciseName,
    string TrackingType,
    bool IsUnilateral,
    int Sessions,
    DateOnly LastPerformedOn,
    IReadOnlyList<PersonalRecordItem> Records);

/// <summary>
/// Evolución de un ejercicio: un punto por día con carga registrada (los últimos 200 como máximo, del más antiguo al
/// más reciente) y los récords sobre todo el historial. Se suma por el ejercicio que de verdad se hizo (R5), no por el planeado.
/// </summary>
public sealed record ExerciseProgressResponse(
    Guid ExerciseId,
    string ExerciseName,
    string TrackingType,
    bool IsUnilateral,
    int Sessions,
    IReadOnlyList<ProgressPoint> Points,
    IReadOnlyList<PersonalRecordItem> Records);

/// <summary>
/// Un día en que el miembro hizo un ejercicio alternativo "en lugar de" el planeado (R5-4). Lo ve el staff en el
/// progreso del miembro.
/// </summary>
public sealed record ExerciseSubstitutionItem(
    DateOnly PerformedOn,
    Guid PlannedExerciseId,
    string PlannedExerciseName,
    Guid PerformedExerciseId,
    string PerformedExerciseName);
