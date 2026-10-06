using System.ComponentModel.DataAnnotations;

namespace GSM.Infrastructure.Attendance;

public sealed class CheckinOptions
{
    public const string SectionName = "Checkin";

    /// <summary>Clave HMAC de los códigos QR del gym. Mínimo 32 caracteres. Nunca en appsettings versionado.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;
}
