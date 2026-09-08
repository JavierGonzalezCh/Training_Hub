using GSM.Application.Common.Abstractions;
using GSM.Domain.Common.Exceptions;

namespace GSM.API.Uploads;

public static class FormFileExtensions
{
    public static FileToStore ToFileToStore(this IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            throw new ValidationDomainException("Falta el archivo.");
        }

        return new FileToStore(file.OpenReadStream(), file.FileName, file.ContentType, file.Length);
    }
}
