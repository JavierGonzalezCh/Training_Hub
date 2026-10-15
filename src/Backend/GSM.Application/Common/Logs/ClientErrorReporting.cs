using FluentValidation;

namespace GSM.Application.Common.Logs;

/// <summary>
/// Un error no controlado del navegador o de la app (docs/GSM-logs.md §4.6). Lo envía el frontend a
/// <c>POST /api/logs/client</c>, con o sin sesión.
/// </summary>
/// <param name="Platform"><c>web_panel</c>, <c>web_member</c>, <c>android</c> o <c>ios</c>.</param>
/// <param name="Route">Pantalla donde pasó (sin consulta: puede llevar códigos).</param>
/// <param name="TraceId">Código de rastreo de la petición a la API que falló, si el error vino de una.</param>
public sealed record ClientErrorRequest(
    string Platform,
    string ExceptionType,
    string Message,
    string? Route = null,
    string? StackTrace = null,
    string? DeviceInfo = null,
    string? TraceId = null,
    string? AppVersion = null);

public static class ClientErrorLimits
{
    public const int Platform = 32;
    public const int Route = 512;
    public const int ExceptionType = 256;
    public const int Message = 4_000;
    public const int StackTrace = 16_000;
    public const int DeviceInfo = 512;
    public const int TraceId = 64;
    public const int AppVersion = 64;

    /// <summary>Tope del cuerpo completo del envío; por encima la API responde 413.</summary>
    public const int MaxBodyBytes = 40_000;
}

public sealed class ClientErrorRequestValidator : AbstractValidator<ClientErrorRequest>
{
    public ClientErrorRequestValidator()
    {
        RuleFor(x => x.Platform).NotEmpty().Must(p => ClientPlatforms.All.Contains(p))
            .WithMessage($"La plataforma debe ser una de: {string.Join(", ", ClientPlatforms.All)}.");
        RuleFor(x => x.ExceptionType).NotEmpty().MaximumLength(ClientErrorLimits.ExceptionType);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(ClientErrorLimits.Message);
        RuleFor(x => x.Route).MaximumLength(ClientErrorLimits.Route);
        RuleFor(x => x.StackTrace).MaximumLength(ClientErrorLimits.StackTrace);
        RuleFor(x => x.DeviceInfo).MaximumLength(ClientErrorLimits.DeviceInfo);
        RuleFor(x => x.TraceId).MaximumLength(ClientErrorLimits.TraceId);
        RuleFor(x => x.AppVersion).MaximumLength(ClientErrorLimits.AppVersion);
    }
}

/// <summary>Caso de uso de <c>POST /api/logs/client</c>: guarda el error por <see cref="ILogStore"/> (que limpia y no espera).</summary>
public sealed class ReportClientErrorHandler(ILogStore logStore)
{
    public Task HandleAsync(ClientErrorRequest request, CancellationToken cancellationToken = default)
    {
        logStore.RecordClientError(new ClientErrorLogEntry(
            request.Platform,
            request.Route,
            request.ExceptionType,
            request.Message,
            request.StackTrace,
            request.DeviceInfo,
            request.TraceId,
            request.AppVersion));
        return Task.CompletedTask;
    }
}
