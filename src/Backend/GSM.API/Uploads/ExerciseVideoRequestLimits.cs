namespace GSM.API.Uploads;

/// <summary>Tope de la petición de un video de ejercicio: los 50 MB del archivo más el margen del multipart.</summary>
public static class ExerciseVideoRequestLimits
{
    public const long MaxRequestBytes = 52 * 1024 * 1024;
}
