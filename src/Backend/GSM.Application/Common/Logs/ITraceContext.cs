namespace GSM.Application.Common.Logs;

/// <summary>
/// Código de rastreo de la petición en curso (docs/GSM-logs.md §3). Fuera de una petición es <c>null</c>.
/// Lo implementa la API leyendo el <c>HttpContext</c>.
/// </summary>
public interface ITraceContext
{
    /// <summary>Identificador completo (32 caracteres hexadecimales), o <c>null</c> sin petición.</summary>
    string? TraceId { get; }
}

/// <summary>IP y agente de usuario de la petición en curso (para <c>security_events</c>). Sin petición, <c>null</c>.</summary>
public interface IClientInfo
{
    string? Ip { get; }

    string? UserAgent { get; }
}

public static class TraceCode
{
    /// <summary>El código corto que ve el usuario ("7F3A2C"): los 6 primeros caracteres en mayúsculas.</summary>
    public static string? Short(string? traceId) =>
        string.IsNullOrWhiteSpace(traceId) ? null : traceId.Trim()[..Math.Min(6, traceId.Trim().Length)].ToUpperInvariant();
}
