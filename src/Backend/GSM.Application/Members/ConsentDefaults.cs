using GSM.Domain.Members;

namespace GSM.Application.Members;

/// <summary>
/// Texto base del sistema para cada autorización, el que rige mientras el gym no publique el suyo.
/// </summary>
/// <remarks>
/// BORRADOR - pendiente de revisión legal (Ley 1581). Son textos breves y neutros, sin cláusulas
/// específicas: el gym debe revisarlos con su asesor y publicar los suyos en Configuración.
/// </remarks>
public static class ConsentDefaults
{
    // BORRADOR - pendiente de revisión legal (Ley 1581).
    public const string DataProcessingText =
        "Autorizo al gimnasio a recolectar, almacenar y usar mis datos personales para gestionar mi " +
        "membresía, mi asistencia y la comunicación con el gimnasio. Entiendo que puedo consultar, " +
        "actualizar o solicitar la supresión de mis datos, y revocar esta autorización en cualquier " +
        "momento. Los datos de salud y las medidas corporales solo se guardan con esta autorización.";

    // BORRADOR - pendiente de revisión legal (Ley 1581).
    public const string LiabilityWaiverText =
        "Declaro que conozco y acepto las normas del gimnasio y que realizo la actividad física bajo " +
        "mi propia responsabilidad. Me comprometo a informar al personal de cualquier condición de " +
        "salud que pueda afectar mi entrenamiento y a usar las instalaciones y los equipos con cuidado.";

    public static string TextFor(ConsentType type) => type switch
    {
        ConsentType.DataProcessing => DataProcessingText,
        ConsentType.LiabilityWaiver => LiabilityWaiverText,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
