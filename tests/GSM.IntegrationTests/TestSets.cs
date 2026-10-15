using GSM.Application.Routines.Contracts;

namespace GSM.IntegrationTests;

/// <summary>Series planeadas de partida para los tests de rutinas.</summary>
internal static class TestSets
{
    /// <summary><paramref name="count"/> series normales de <paramref name="repsMin"/>–<paramref name="repsMax"/> repeticiones.</summary>
    public static List<SetTargetInput> Normal(int count, int repsMin, int? repsMax = null)
        => Enumerable.Range(0, count)
            .Select(_ => new SetTargetInput("normal", repsMin, repsMax ?? repsMin, null, null, null, null))
            .ToList();
}
