namespace GSM.Domain.Attendance;

/// <summary>
/// Resultado de la comprobación de ubicación de una entrada con el QR fijo (A6). Nunca se guardan las
/// coordenadas, solo este resultado. Una entrada sin comprobación (QR rotativo, documento, recepción, o un gym
/// sin coordenadas) lleva <c>null</c> y cuenta para logros y racha como siempre.
/// </summary>
public enum LocationCheck
{
    /// <summary>Estaba dentro del radio del gym.</summary>
    Inside,

    /// <summary>Compartió su ubicación y estaba fuera del radio.</summary>
    Outside,

    /// <summary>No compartió su ubicación (negó el permiso, falló el GPS o no la envió).</summary>
    Denied,
}
