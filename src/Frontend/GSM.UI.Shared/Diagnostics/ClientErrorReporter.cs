using GSM.Client.Errors;
using GSM.Client.Generated;
using Microsoft.AspNetCore.Components;

namespace GSM.UI.Shared.Diagnostics;

/// <summary>Valores de <c>platform</c> que acepta <c>POST /api/logs/client</c> (espejo de <c>ClientPlatforms</c> del backend).</summary>
public static class ClientPlatformNames
{
    public const string WebPanel = "web_panel";
    public const string WebMember = "web_member";
    public const string Android = "android";
    public const string Ios = "ios";
}

/// <summary>Lo que cada host (navegador, app nativa) sabe de sí mismo para el reporte de errores.</summary>
public interface IClientEnvironment
{
    /// <summary>
    /// Plataforma del reporte (<c>web_panel</c>, <c>web_member</c>, <c>android</c>, <c>ios</c>) según la ruta
    /// actual, o <c>null</c> si este host no se reporta (p. ej. Windows nativo: no es una plataforma de la spec).
    /// </summary>
    string? ResolvePlatform(string route);

    /// <summary>Navegador o modelo y versión del sistema.</summary>
    Task<string?> GetDeviceInfoAsync();

    string? AppVersion { get; }
}

/// <summary>
/// Reporta a <c>POST /api/logs/client</c> los errores no controlados de la interfaz (docs/GSM-logs.md §4.6).
/// Nunca lanza, no espera a la red y limita lo que envía: un máximo por sesión y sin repetir el mismo error.
/// <see cref="Report"/> devuelve el código corto que se le muestra al usuario ("Algo salió mal · código 7F3A2C").
/// </summary>
public sealed class ClientErrorReporter(ILogsClient logs, IClientEnvironment environment, NavigationManager navigation)
{
    public const int MaxPerSession = 20;

    private readonly Dictionary<string, string> _seen = new(StringComparer.Ordinal);
    private int _sent;

    /// <returns>El código corto de rastreo del error, o <c>null</c> si no se pudo calcular.</returns>
    public string? Report(Exception exception)
    {
        try
        {
            // Un fallo de la API ya quedó en el servidor con su traceId: se muestra ese código y no se duplica.
            if (exception is GsmApiException api)
            {
                return ApiError.FromException(api).TraceCode;
            }

            var fingerprint = exception.GetType().FullName + "|" + exception.Message;
            if (_seen.TryGetValue(fingerprint, out var known))
            {
                return known;
            }

            // El cliente fija el código del envío para poder mostrárselo al usuario; el servidor lo guarda como traceId.
            var traceId = Guid.NewGuid().ToString("N");
            var code = traceId[..6].ToUpperInvariant();
            _seen[fingerprint] = code;

            if (_sent >= MaxPerSession)
            {
                return code;
            }

            _sent++;
            var route = "/" + navigation.ToBaseRelativePath(navigation.Uri);
            _ = SendAsync(exception, route, traceId);
            return code;
        }
        catch
        {
            return null;
        }
    }

    private async Task SendAsync(Exception exception, string route, string traceId)
    {
        try
        {
            var platform = environment.ResolvePlatform(route);
            if (platform is null)
            {
                return;
            }

            await logs.ReportClientErrorAsync(new ClientErrorRequest
            {
                Platform = platform,
                Route = route,
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                Message = exception.Message,
                StackTrace = exception.StackTrace,
                DeviceInfo = await environment.GetDeviceInfoAsync(),
                TraceId = traceId,
                AppVersion = environment.AppVersion,
            });
        }
        catch
        {
            // Reportar no puede fallar a la pantalla que ya está fallando.
        }
    }
}
