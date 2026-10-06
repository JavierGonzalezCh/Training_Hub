namespace GSM.Domain.Routines;

/// <summary>Una serie realizada: su número (1..n), las repeticiones y el peso que usó el socio.</summary>
public sealed record RoutineSetEntry(int Number, int? Reps, decimal? WeightKg);
