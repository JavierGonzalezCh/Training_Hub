namespace GSM.Infrastructure.Notifications.Push;

/// <summary>
/// Configuración de Firebase Cloud Messaging. Sección <c>Notifications:Fcm</c>. Un proyecto de Firebase por
/// ambiente; la cuenta de servicio va en la configuración (variable de entorno o secreto), nunca en el código.
/// Sin <see cref="ProjectId"/> y <see cref="ServiceAccountJson"/> el push se omite y la API arranca igual.
/// </summary>
public sealed class FcmOptions
{
    public const string SectionName = "Notifications:Fcm";

    public string ProjectId { get; init; } = string.Empty;

    /// <summary>Contenido del JSON de la cuenta de servicio (<c>client_email</c>, <c>private_key</c>, <c>token_uri</c>).</summary>
    public string ServiceAccountJson { get; init; } = string.Empty;

    /// <summary>Datos públicos del SDK web de Firebase (la PWA los pide a <c>GET /api/push/config</c>).</summary>
    public FcmWebOptions Web { get; init; } = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ServiceAccountJson);
}

/// <summary>Configuración pública del cliente web: no son secretos (van en el navegador).</summary>
public sealed class FcmWebOptions
{
    public string ApiKey { get; init; } = string.Empty;

    public string AppId { get; init; } = string.Empty;

    public string MessagingSenderId { get; init; } = string.Empty;

    /// <summary>Clave pública VAPID (Web Push certificate) del proyecto.</summary>
    public string VapidKey { get; init; } = string.Empty;
}
