using System.Net.Http.Json;
using GSM.Client.Generated;

namespace GSM.Mobile.Platform;

/// <summary>
/// Cierres inesperados de la app nativa (docs/GSM-logs.md §4.6): excepciones que ningún componente Blazor
/// atrapó (<c>AppDomain.UnhandledException</c>, tareas sin observar). Corre fuera de cualquier ámbito de DI,
/// así que envía el reporte con un <see cref="HttpClient"/> propio y sin sesión (el endpoint es anónimo).
/// Best-effort: un proceso que se cae puede no alcanzar a enviarlo.
/// </summary>
public static class MauiCrashReporter
{
    private static string _apiBaseUrl = "";
    private static int _installed;

    public static void Install(string apiBaseUrl)
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Report(exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Report(e.Exception);
    }

    private static void Report(Exception exception)
    {
        try
        {
            var platform = MauiClientEnvironment.PlatformName();
            if (platform is null)
            {
                return;
            }

            var body = new ClientErrorRequest
            {
                Platform = platform,
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                Message = exception.Message,
                StackTrace = exception.StackTrace,
                DeviceInfo = MauiClientEnvironment.DescribeDevice(),
                TraceId = Guid.NewGuid().ToString("N"),
                AppVersion = Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString,
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.PostAsJsonAsync($"{_apiBaseUrl}/api/logs/client", body).Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Reportar no puede provocar otro cierre.
        }
    }
}
