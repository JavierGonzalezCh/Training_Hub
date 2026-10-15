namespace GSM.Domain.Routines;

/// <summary>
/// Una serie realizada: su número (1..n) y lo que hizo el miembro. Simétrica con <see cref="SetTarget"/>:
/// se compara serie contra serie. Solo se llenan los campos que corresponden al <c>tracking_type</c> del
/// ejercicio. Las repeticiones de un ejercicio unilateral son por lado.
/// </summary>
public sealed record RoutineSetEntry(
    int Number,
    SetType SetType,
    int? Reps,
    decimal? WeightKg,
    int? DurationSeconds,
    int? DistanceM,
    int? Rir);
