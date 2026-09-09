namespace GSM.Application.Routines;

/// <summary>
/// Ciclo rotativo: los <c>N</c> días de la rutina se repiten desde <c>startsOn</c>. Función pura,
/// sin estado — la racha se recomputa entera en cualquier momento sin haber guardado nada.
/// </summary>
public static class RoutineCycle
{
    /// <summary>
    /// Posición (1..<paramref name="cycleLength"/>) que toca en <paramref name="date"/>.
    /// <paramref name="date"/> puede ser anterior a <paramref name="startsOn"/>: la aritmética
    /// de módulo se normaliza para no dar negativos.
    /// </summary>
    public static int PositionOn(DateOnly startsOn, int cycleLength, DateOnly date)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cycleLength, 1);

        var offset = date.DayNumber - startsOn.DayNumber;
        var mod = ((offset % cycleLength) + cycleLength) % cycleLength;
        return mod + 1;
    }
}
