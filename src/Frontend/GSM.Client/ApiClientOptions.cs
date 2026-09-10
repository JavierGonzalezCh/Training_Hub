namespace GSM.Client;

/// <summary>Configuración del cliente. <see cref="ApiBaseUrl"/> sale de <c>wwwroot/appsettings.json</c>.</summary>
public sealed class ApiClientOptions
{
    public const string SectionName = "Api";

    public string ApiBaseUrl { get; set; } = string.Empty;
}
