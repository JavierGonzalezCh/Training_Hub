using System.Text.Json;
using GSM.Client.Generated;

namespace GSM.Client.Errors;

/// <summary>
/// Traducción tipada de las respuestas de error de la API. El backend responde siempre
/// <c>ProblemDetails</c> (RFC 7807) o <c>ValidationProblemDetails</c>, salvo el 429 del rate limit.
/// Convención de status (ver <c>CLAUDE.md</c>):
/// <list type="bullet">
///   <item>400 — errores de forma; los detalles por campo van en <see cref="FieldErrors"/>.</item>
///   <item>422 — invariante de negocio; el motivo va en <see cref="Detail"/>.</item>
///   <item>409 — conflicto de estado (duplicado, cupo lleno).</item>
///   <item>403 — falta de permiso <b>o</b> bloqueo por suscripción (mismo <c>title</c>;
///         se distingue mirando <c>subscription.status</c> del bootstrap).</item>
///   <item>401 — sesión caducada; lo resuelve <see cref="Http.AuthenticatedHttpHandler"/>.</item>
///   <item>429 — rate limit de login/registro/refresh.</item>
/// </list>
/// </summary>
public sealed class ApiError
{
    public required int Status { get; init; }

    public string? Title { get; init; }

    public string? Detail { get; init; }

    /// <summary>Errores por campo de un 400 (<c>ValidationProblemDetails.errors</c>), en minúsculas camelCase.</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; init; } =
        new Dictionary<string, string[]>();

    public bool IsValidation => Status == 400 && FieldErrors.Count > 0;

    public bool IsUnprocessable => Status == 422;

    public bool IsConflict => Status == 409;

    public bool IsForbidden => Status == 403;

    public bool IsUnauthorized => Status == 401;

    public bool IsRateLimited => Status == 429;

    /// <summary>Mensaje razonable para mostrar al usuario cuando no hay un tratamiento específico.</summary>
    public string DisplayMessage => Status switch
    {
        429 => "Demasiados intentos. Espera un minuto e inténtalo de nuevo.",
        401 => "Tu sesión ha caducado. Vuelve a iniciar sesión.",
        403 => Detail ?? Title ?? "No tienes permiso para esta operación.",
        409 => Detail ?? Title ?? "La operación entra en conflicto con el estado actual.",
        422 => Detail ?? Title ?? "La operación no procede en este estado.",
        >= 500 => "Ha ocurrido un error en el servidor. Inténtalo más tarde.",
        _ => Detail ?? Title ?? "No se ha podido completar la operación.",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Convierte la excepción del cliente generado en un <see cref="ApiError"/>.</summary>
    public static ApiError FromException(GsmApiException exception)
        => Parse(exception.StatusCode, exception.Response);

    public static ApiError Parse(int status, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ApiError { Status = status };
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var fieldErrors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errors.EnumerateObject())
                {
                    fieldErrors[property.Name] = property.Value.ValueKind == JsonValueKind.Array
                        ? property.Value.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray()
                        : [property.Value.GetString() ?? string.Empty];
                }
            }

            return new ApiError
            {
                Status = root.TryGetProperty("status", out var s) && s.TryGetInt32(out var code) ? code : status,
                Title = root.TryGetProperty("title", out var t) ? t.GetString() : null,
                Detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null,
                FieldErrors = fieldErrors,
            };
        }
        catch (JsonException)
        {
            return new ApiError { Status = status, Detail = body };
        }
    }
}

/// <summary>
/// Excepción de aplicación que envuelve un <see cref="ApiError"/> ya traducido.
/// La UI la captura y decide el tratamiento según el status.
/// </summary>
public sealed class ApiErrorException(ApiError error)
    : Exception(error.DisplayMessage)
{
    public ApiError Error { get; } = error;
}
