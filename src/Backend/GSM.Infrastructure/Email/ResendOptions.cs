namespace GSM.Infrastructure.Email;

/// <summary>Config de Resend. Sección <c>Email:Resend</c>.</summary>
public sealed class ResendOptions
{
    public const string SectionName = "Email:Resend";

    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Remitente verificado en Resend, p. ej. "GSM &lt;no-reply@tudominio.com&gt;".</summary>
    public string FromAddress { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(FromAddress);
}
