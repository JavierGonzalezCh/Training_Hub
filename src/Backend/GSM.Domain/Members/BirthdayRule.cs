namespace GSM.Domain.Members;

/// <summary>
/// Cuándo se celebra un cumpleaños. Quien nació el 29 de febrero lo celebra el 28 en los años que no son
/// bisiestos (Comunicaciones §F). Una sola regla para la tarea de felicitaciones, el dashboard y recepción.
/// </summary>
public static class BirthdayRule
{
    /// <summary>¿El día <paramref name="day"/> es el cumpleaños de quien nació en <paramref name="birthDate"/>?</summary>
    public static bool IsBirthday(DateOnly birthDate, DateOnly day)
    {
        if (birthDate.Month == day.Month && birthDate.Day == day.Day)
        {
            return true;
        }

        return CelebratesFeb29OnFeb28(day) && birthDate is { Month: 2, Day: 29 };
    }

    /// <summary>¿Es el 28 de febrero de un año no bisiesto, día en que se celebra a los nacidos el 29?</summary>
    public static bool CelebratesFeb29OnFeb28(DateOnly day)
        => day is { Month: 2, Day: 28 } && !DateTime.IsLeapYear(day.Year);
}
