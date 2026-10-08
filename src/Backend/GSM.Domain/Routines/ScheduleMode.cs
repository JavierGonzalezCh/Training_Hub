namespace GSM.Domain.Routines;

/// <summary>
/// Cómo se calendariza una rutina. Se elige al crearla y no cambia: pasar de uno a otro crea una rutina nueva.
/// </summary>
public enum ScheduleMode
{
    /// <summary>Ciclo rotativo Día 1…N desde <c>StartsOn</c>, sin atarse a la semana (el descanso es una fila).</summary>
    Rotating,

    /// <summary>Cada día de entrenamiento se ata a un día de la semana; el día sin fila es descanso.</summary>
    Weekly,
}

/// <summary>Día de la semana ISO: 1 = lunes … 7 = domingo.</summary>
public static class IsoWeekDay
{
    public const int Monday = 1;
    public const int Sunday = 7;

    public static int Of(DateOnly date)
        => date.DayOfWeek == DayOfWeek.Sunday ? Sunday : (int)date.DayOfWeek;
}
