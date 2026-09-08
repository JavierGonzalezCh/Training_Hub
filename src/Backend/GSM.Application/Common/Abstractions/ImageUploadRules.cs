using GSM.Application.Common.Abstractions;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Common;

/// <summary>Validación común para las subidas de imagen: tipo MIME permitido y tamaño máximo.</summary>
public static class ImageUploadRules
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp",
    };

    public static void EnsureValid(FileToStore file)
    {
        if (file.Length <= 0)
        {
            throw new ValidationDomainException("El archivo está vacío.");
        }

        if (file.Length > MaxBytes)
        {
            throw new ValidationDomainException("El archivo supera el máximo de 5 MB.");
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            throw new ValidationDomainException("Solo se aceptan imágenes JPEG, PNG o WEBP.");
        }
    }
}
