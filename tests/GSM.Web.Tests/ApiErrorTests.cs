using GSM.Client.Errors;

namespace GSM.Web.Tests;

public class ApiErrorTests
{
    [Fact]
    public void Parses_400_field_errors_from_validation_problem_details()
    {
        const string body = """
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          "title": "One or more validation errors occurred.",
          "status": 400,
          "errors": {
            "email": ["El email es obligatorio."],
            "password": ["Mínimo 8 caracteres.", "Debe incluir un número."]
          }
        }
        """;

        var error = ApiError.Parse(400, body);

        Assert.Equal(400, error.Status);
        Assert.True(error.IsValidation);
        Assert.Equal(2, error.FieldErrors.Count);
        Assert.Equal("El email es obligatorio.", error.FieldErrors["email"].Single());
        Assert.Equal(2, error.FieldErrors["password"].Length);
    }

    [Fact]
    public void Parses_422_detail_from_problem_details()
    {
        const string body = """
        {
          "title": "Invariante de negocio violada",
          "status": 422,
          "detail": "La membresía ya está activa."
        }
        """;

        var error = ApiError.Parse(422, body);

        Assert.True(error.IsUnprocessable);
        Assert.Equal("La membresía ya está activa.", error.Detail);
        Assert.Equal("La membresía ya está activa.", error.DisplayMessage);
        Assert.Empty(error.FieldErrors);
    }

    [Fact]
    public void Parses_409_conflict()
    {
        const string body = """
        { "title": "Conflicto de estado", "status": 409, "detail": "El documento ya existe." }
        """;

        var error = ApiError.Parse(409, body);

        Assert.True(error.IsConflict);
        Assert.Equal("El documento ya existe.", error.DisplayMessage);
    }

    [Fact]
    public void Parses_403_forbidden_or_subscription_block()
    {
        const string body = """
        { "title": "Operación no permitida", "status": 403 }
        """;

        var error = ApiError.Parse(403, body);

        Assert.True(error.IsForbidden);
        Assert.Equal("Operación no permitida", error.DisplayMessage);
    }

    [Fact]
    public void Handles_429_rate_limit_without_body()
    {
        var error = ApiError.Parse(429, null);

        Assert.True(error.IsRateLimited);
        Assert.Contains("Demasiados intentos", error.DisplayMessage);
    }

    [Fact]
    public void FromException_maps_status_and_body()
    {
        var ex = new GSM.Client.Generated.GsmApiException(
            "boom", 422, """{ "status": 422, "detail": "No procede." }""",
            new Dictionary<string, IEnumerable<string>>(), null);

        var error = ApiError.FromException(ex);

        Assert.Equal(422, error.Status);
        Assert.Equal("No procede.", error.Detail);
    }

    [Fact]
    public void Server_error_shows_the_short_trace_code()
    {
        var error = ApiError.Parse(500, """{ "status": 500, "title": "Error", "traceId": "7f3a2cdeadbeef0123456789abcdef01" }""");

        Assert.Equal("7f3a2cdeadbeef0123456789abcdef01", error.TraceId);
        Assert.Equal("7F3A2C", error.TraceCode);
        Assert.Equal("Algo salió mal · código 7F3A2C", error.DisplayMessage);
    }

    [Fact]
    public void Trace_id_in_w3c_format_is_normalized()
    {
        var error = ApiError.Parse(500, """{ "traceId": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01" }""");

        Assert.Equal("0af7651916cd43dd8448eb211c80319c", error.TraceId);
        Assert.Equal("0AF765", error.TraceCode);
    }

    [Fact]
    public void Server_error_without_trace_id_keeps_the_generic_message()
    {
        Assert.Contains("error en el servidor", ApiError.Parse(500, null).DisplayMessage);
    }
}
