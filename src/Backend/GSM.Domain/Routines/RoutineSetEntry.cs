namespace GSM.Domain.Routines;

/// <summary>Una serie realizada: su número (1..n), las repeticiones y el peso que usó el miembro.</summary>
public sealed record RoutineSetEntry(int Number, int? Reps, decimal? WeightKg);
