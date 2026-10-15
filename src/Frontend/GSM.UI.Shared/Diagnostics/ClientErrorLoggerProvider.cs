using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GSM.UI.Shared.Diagnostics;

/// <summary>
/// Manejador global: Blazor registra por <c>ILogger</c> las excepciones no controladas de sus componentes
/// ("Unhandled exception rendering component", handlers de eventos, JS interop). Este proveedor las reenvía a
/// <see cref="ClientErrorReporter"/>. Solo mira las categorías de Blazor, así que los fallos del propio reporte
/// (HttpClient) no pueden provocar un bucle.
/// </summary>
public sealed class ClientErrorLoggerProvider(IServiceProvider services) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
        => categoryName.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal)
            ? new ReportingLogger(services)
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class ReportingLogger(IServiceProvider services) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is null || logLevel < LogLevel.Error)
            {
                return;
            }

            try
            {
                // En WASM la raíz es el único ámbito de la app; en otros hosts el servicio de ámbito puede no resolverse.
                services.GetService<ClientErrorReporter>()?.Report(exception);
            }
            catch
            {
                // nada más que hacer
            }
        }
    }
}
