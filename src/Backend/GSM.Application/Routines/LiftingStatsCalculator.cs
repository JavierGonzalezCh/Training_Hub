namespace GSM.Application.Routines;

/// <summary>Récords personales batidos y volumen levantado de un miembro (L3).</summary>
public sealed record LiftingStats(int PersonalRecords, decimal LiftedVolumeKg);

/// <summary>
/// Cálculo puro para los logros de récords y volumen (L3). Recibe los registros de ejercicios de repeticiones con peso
/// y repite el mismo criterio de R7 (<see cref="MemberProgressCalculator.BrokenKinds"/>) día a día: un récord es un día
/// en que se supera lo mejor de los días anteriores en peso, volumen de serie o volumen de sesión; el primer día de
/// cada ejercicio es línea base. Un día que bate varias marcas del mismo ejercicio cuenta como un récord.
/// El volumen es peso × repeticiones de todas las series que no son calentamiento.
/// </summary>
public static class LiftingStatsCalculator
{
    public static LiftingStats Compute(IEnumerable<(Guid ExerciseId, ProgressLog Log)> logs)
    {
        var records = 0;
        var volume = 0m;

        foreach (var group in logs.GroupBy(l => l.ExerciseId))
        {
            var points = MemberProgressCalculator.Points(group.Select(l => l.Log));
            volume += points.Sum(p => p.TotalVolume ?? 0m);

            for (var i = 1; i < points.Count; i++)
            {
                var previous = points.Take(i).ToList();
                if (MemberProgressCalculator.BrokenKinds(previous, null, points[i]).Count > 0)
                {
                    records++;
                }
            }
        }

        return new LiftingStats(records, volume);
    }
}
