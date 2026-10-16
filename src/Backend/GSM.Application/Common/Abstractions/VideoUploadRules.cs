using GSM.Application.Common.Abstractions;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Common;

/// <summary>Validación común para las subidas de video corto (R6): solo MP4, hasta 50 MB.</summary>
public static class VideoUploadRules
{
    public const long MaxBytes = 50 * 1024 * 1024;

    public static void EnsureValid(FileToStore file)
    {
        if (file.Length <= 0)
        {
            throw new ValidationDomainException("El archivo está vacío.");
        }

        if (file.Length > MaxBytes)
        {
            throw new ValidationDomainException("El video supera el máximo de 50 MB.");
        }

        if (!string.Equals(file.ContentType, "video/mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationDomainException("Solo se aceptan videos MP4.");
        }
    }
}
