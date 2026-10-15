using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using GSM.Application.Common.Events;
using GSM.Application.Common.Logs;
using GSM.Infrastructure.Logs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// LG2: <c>business_rejections</c>, <c>security_events</c> y <c>slow_requests</c> (docs/GSM-logs.md §4.2, §4.7, §4.8).
/// Reutiliza el controlador de prueba de <see cref="LogsTests"/>. La prueba de purga y particiones de estas tablas
/// vive en <see cref="LogsTests"/> para no competir por el latido <c>logs_purge</c>.
/// </summary>
public sealed class LogsRejectionsSecuritySlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private WebApplicationFactory<Program> TestHost(params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(LogTestController).Assembly);
                services.AddScoped<IValidator<LogTestDto>, LogTestDtoValidator>();
                services.AddScoped<IDomainEventHandler<LogTestEvent>, FailingLogTestEventHandler>();
            });
        });

    private static string TraceOf(HttpResponseMessage response) => response.Headers.GetValues("X-Trace-Id").Single();

    private static LogsDbContext OpenLogs(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<IDbContextFactory<LogsDbContext>>().CreateDbContext();

    // ------------------------------------------------------------------ rechazos de negocio

    [Theory]
    [InlineData("/test-logs/conflict", 409, "ConflictException")]
    [InlineData("/test-logs/unprocessable", 422, "ValidationDomainException")]
    [InlineData("/test-logs/missing", 404, "NotFoundException")]
    public async Task Domain_exception_leaves_a_business_rejection_with_the_trace_id(
        string path, int status, string exceptionType)
    {
        await using var host = TestHost();
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(status, (int)response.StatusCode);
        var traceId = TraceOf(response);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var rows = await db.BusinessRejections.Where(r => r.TraceId == traceId).ToListAsync();

        var row = Assert.Single(rows);                       // una sola fila: el middleware no la duplica
        Assert.Equal(status, row.StatusCode);
        Assert.Equal(exceptionType, row.ExceptionType);
        Assert.Equal("GET", row.Method);
        Assert.Equal(path, row.Endpoint);
        Assert.False(string.IsNullOrEmpty(row.Title));
        Assert.False(string.IsNullOrEmpty(row.Detail));
        Assert.Null(row.FieldErrors);
    }

    [Fact]
    public async Task Form_validation_400_records_only_the_names_of_the_failed_fields()
    {
        await using var host = TestHost();
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();

        var response = await client.PostAsJsonAsync("/test-logs/validate", new { name = "", password = "hunter2-secret" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var traceId = TraceOf(response);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var row = await db.BusinessRejections.SingleAsync(r => r.TraceId == traceId);

        Assert.Equal(400, row.StatusCode);
        Assert.Equal("validation", row.ExceptionType);
        Assert.Equal("/test-logs/validate", row.Endpoint);
        Assert.Contains("Name", row.FieldErrors);
        Assert.DoesNotContain("hunter2-secret", row.FieldErrors);
    }

    // ------------------------------------------------------------------ seguridad

    [Fact]
    public async Task Failed_login_leaves_a_security_event_without_the_password()
    {
        await using var host = TestHost();
        var client = host.CreateClient();
        var email = $"nadie-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Sup3r-Secreta-Clave" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var traceId = TraceOf(response);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var row = await db.SecurityEvents.SingleAsync(e => e.TraceId == traceId);

        Assert.Equal("login_failed", row.EventType);
        Assert.Contains(email, row.Detail);
        Assert.DoesNotContain("Sup3r-Secreta-Clave", row.Detail ?? "");
        // TestServer no tiene IP remota: la IP no se puede comprobar aquí.
        Assert.Equal(32, row.TraceId!.Length);
    }

    [Fact]
    public async Task Invalid_bearer_token_leaves_an_invalid_token_event_without_the_token()
    {
        await using var host = TestHost();
        var client = host.CreateClient();
        const string fakeToken = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.firma-falsa-para-test";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fakeToken);

        var response = await client.GetAsync("/api/auth/sessions-that-do-not-exist");   // cualquier ruta: la autenticación corre antes
        var traceId = TraceOf(response);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var row = await db.SecurityEvents.SingleAsync(e => e.TraceId == traceId && e.EventType == "invalid_token");

        Assert.DoesNotContain("firma-falsa", row.Detail ?? "");
        Assert.DoesNotContain(fakeToken, row.UserAgent ?? "");
    }

    [Fact]
    public async Task Failed_kiosk_pairing_leaves_an_event_without_the_code()
    {
        await using var host = TestHost();
        var client = host.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/kiosk/pair", new { pairingCode = "ZZZZ-9999" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var traceId = TraceOf(response);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var row = await db.SecurityEvents.SingleAsync(e => e.TraceId == traceId);

        Assert.Equal("kiosk_pairing_failed", row.EventType);
        Assert.DoesNotContain("ZZZZ-9999", row.Detail ?? "");
    }

    [Fact]
    public async Task Exceeding_the_login_rate_limit_leaves_a_rate_limited_event()
    {
        await using var host = TestHost();
        var client = host.CreateClient();
        HttpResponseMessage? limited = null;

        for (var i = 0; i < 15 && limited is null; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { email = $"rl-{i}-{Guid.NewGuid():N}@example.com", password = "x" });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
        }

        Assert.NotNull(limited);
        var traceId = TraceOf(limited!);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var row = await db.SecurityEvents.SingleAsync(e => e.TraceId == traceId);
        Assert.Equal("rate_limited", row.EventType);
    }

    // ------------------------------------------------------------------ lentitud

    [Fact]
    public async Task Request_over_the_threshold_leaves_a_slow_request_row()
    {
        await using var host = TestHost(("Logs:SlowRequestMs", "0"));
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();

        var response = await client.PostAsJsonAsync("/test-logs/validate", new { name = "Ana", password = "x" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var traceId = TraceOf(response);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var row = await db.SlowRequests.SingleAsync(r => r.TraceId == traceId);

        Assert.Equal("POST", row.Method);
        Assert.Equal("/test-logs/validate", row.Endpoint);
        Assert.Equal(200, row.StatusCode);
        Assert.True(row.DurationMs >= 0);
    }

    [Fact]
    public async Task Fast_request_under_the_default_threshold_leaves_no_slow_request_row()
    {
        await using var host = TestHost();
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();

        var response = await client.PostAsJsonAsync("/test-logs/validate", new { name = "Ana", password = "x" });
        var traceId = TraceOf(response);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        Assert.False(await db.SlowRequests.AnyAsync(r => r.TraceId == traceId));
    }
}
