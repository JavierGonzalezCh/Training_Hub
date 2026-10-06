namespace GSM.Domain.Attendance;

/// <summary>Tipo de QR que el gym muestra en la entrada.</summary>
public enum CheckinMode
{
    /// <summary>Pantalla en la entrada: el código cambia cada 30 s.</summary>
    Rotating,

    /// <summary>Cartel impreso: código fijo, regenerable.</summary>
    Static,
}
