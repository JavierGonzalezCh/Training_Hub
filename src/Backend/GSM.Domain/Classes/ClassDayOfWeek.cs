namespace GSM.Domain.Classes;

/// <summary>
/// Día de la semana en el que se repite una clase recurrente. Enum nativo de Postgres
/// (<c>classes.day_of_week</c>), label en minúsculas por <c>LowerCaseNameTranslator</c>.
/// </summary>
public enum ClassDayOfWeek
{
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
    Sunday,
}
