using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using GSM.API.Filters;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Common.Logs;
using GSM.Domain.Common.Exceptions;
using GSM.Infrastructure.Logs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

// ---- Artefactos de prueba: un controlador que falla a propósito y un evento cuyo manejador lanza. ----

public sealed record LogTestDto(string Name, string Password);

public sealed class LogTestDtoValidator : AbstractValidator<LogTestDto>
{
    public LogTestDtoValidator() => RuleFor(x => x.Name).NotEmpty();
}

public sealed record LogTestEvent(Guid Id, string Name, string Password) : IDomainEvent;

public sealed class FailingLogTestEventHandler : IDomainEventHandler<LogTestEvent>
{
    public Task HandleAsync(LogTestEvent domainEvent, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("el manejador de prueba falla a propósito");
}

[ApiController]
[Route("test-logs")]
[AllowAnonymous]
[SubscriptionGateExempt]
public sealed class LogTestController : ControllerBase
{
    [HttpPost("boom/{code}")]
    public IActionResult Boom(string code, [FromBody] LogTestDto body)
        => throw new InvalidOperationException("boom provocado por el test");

    [HttpPost("validate")]
    public IActionResult Validate([FromBody] LogTestDto body) => Ok();

    [HttpGet("conflict")]
    public IActionResult ConflictCase() => throw new ConflictException("choque de prueba");

    [HttpGet("unprocessable")]
    public IActionResult UnprocessableCase() => throw new ValidationDomainException("regla de prueba");

    [HttpGet("missing")]
    public IActionResult MissingCase() => throw new NotFoundException("no está");

    [HttpPost("event")]
    public async Task<IActionResult> Event([FromServices] IDomainEventPublisher publisher)
    {
        await publisher.PublishAsync(new LogTestEvent(Guid.NewGuid(), "evento", "s3cret-pass"));
        return Ok(new { done = true });
    }
}

/// <summary>LG1: el schema <c>logs</c>, el código de rastreo y los registros de errores, eventos y tareas.</summary>
public sealed class LogsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string JobSecret = "integration-tests-jobs-secret-32-bytes-min!!";

    private static readonly string BrokenLogsConnection =
        "Host=localhost;Port=1;Database=nope;Username=nope;Password=nope;Timeout=2;Command Timeout=2";

    private WebApplicationFactory<Program> TestHost(string? logsConnection = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (logsConnection is not null)
            {
                builder.UseSetting("ConnectionStrings:Logs", logsConnection);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(LogTestController).Assembly);
                services.AddScoped<IValidator<LogTestDto>, LogTestDtoValidator>();
                services.AddScoped<IDomainEventHandler<LogTestEvent>, FailingLogTestEventHandler>();
            });
        });

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static LogsDbContext OpenLogs(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<IDbContextFactory<LogsDbContext>>().CreateDbContext();

    // ------------------------------------------------------------------ 1. un 500

    [Fact]
    public async Task Unhandled_exception_returns_problem_with_trace_id_and_logs_a_clean_error_row()
    {
        await using var host = TestHost();
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();

        var response = await client.PostAsJsonAsync("/test-logs/boom/INVITE-9X?token=abc&page=2",
            new { name = "Ana", password = "hunter2-secret" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        var traceId = problem.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrEmpty(traceId));
        Assert.Equal(32, traceId!.Length);
        Assert.Equal(traceId, response.Headers.GetValues("X-Trace-Id").Single());
        Assert.DoesNotContain("boom provocado", await response.Content.ReadAsStringAsync());   // el detalle no sale

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var row = await db.Errors.SingleAsync(e => e.TraceId == traceId);

        Assert.Equal("api", row.Source);
        Assert.Equal("POST", row.Method);
        Assert.Equal("/test-logs/boom/{code}", row.Endpoint);
        Assert.Equal("System.InvalidOperationException", row.ExceptionType);
        Assert.Contains("boom provocado", row.Message);
        Assert.Equal(32, row.Fingerprint.Length);
        Assert.DoesNotContain("INVITE-9X", row.Path);            // el valor de la ruta "code" se oculta
        Assert.Contains("Ana", row.RequestSummary);
        Assert.DoesNotContain("hunter2-secret", row.RequestSummary);
        Assert.DoesNotContain("INVITE-9X", row.RequestSummary);
        Assert.DoesNotContain("abc", row.RequestSummary!.Replace("page", ""));   // token de la consulta
        Assert.Contains(LogSanitizer.Redacted, row.RequestSummary);
        Assert.True(row.RequestSummary.Length <= LogSanitizer.RequestSummaryMaxChars + 20);
        Assert.Equal("Testing", row.Environment);
    }

    [Fact]
    public async Task Same_failure_produces_the_same_fingerprint()
    {
        await using var host = TestHost();
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();

        var first = await ReadJsonAsync(await client.PostAsJsonAsync("/test-logs/boom/a", new { name = "x", password = "p" }));
        var second = await ReadJsonAsync(await client.PostAsJsonAsync("/test-logs/boom/b", new { name = "y", password = "q" }));

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var ids = new[] { first.GetProperty("traceId").GetString(), second.GetProperty("traceId").GetString() };
        var fingerprints = await db.Errors.Where(e => ids.Contains(e.TraceId)).Select(e => e.Fingerprint).Distinct().ToListAsync();

        Assert.Single(fingerprints);
    }

    // ------------------------------------------------------------------ 2. 4xx con traceId, sin fila

    [Theory]
    [InlineData("/test-logs/conflict", HttpStatusCode.Conflict)]
    [InlineData("/test-logs/unprocessable", HttpStatusCode.UnprocessableEntity)]
    [InlineData("/test-logs/missing", HttpStatusCode.NotFound)]
    public async Task Domain_rejections_carry_a_trace_id_and_are_not_logged_as_errors(string url, HttpStatusCode expected)
    {
        await using var host = TestHost();
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(expected, response.StatusCode);
        var traceId = (await ReadJsonAsync(response)).GetProperty("traceId").GetString();
        Assert.Equal(32, traceId!.Length);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        Assert.False(await db.Errors.AnyAsync(e => e.TraceId == traceId));
    }

    [Fact]
    public async Task Validation_400_carries_a_trace_id()
    {
        await using var host = TestHost();
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();

        var response = await client.PostAsJsonAsync("/test-logs/validate", new { name = "", password = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(32, body.GetProperty("traceId").GetString()!.Length);
        Assert.True(body.TryGetProperty("errors", out _));
    }

    // ------------------------------------------------------------------ 3. la base de logs cae

    [Fact]
    public async Task Request_still_responds_when_the_logs_database_is_unreachable()
    {
        await using var host = TestHost(BrokenLogsConnection);
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();

        var failing = await client.PostAsJsonAsync("/test-logs/boom/x", new { name = "a", password = "b" });
        Assert.Equal(HttpStatusCode.InternalServerError, failing.StatusCode);
        Assert.False(string.IsNullOrEmpty((await ReadJsonAsync(failing)).GetProperty("traceId").GetString()));

        var healthy = await client.GetAsync("/test-logs/conflict");
        Assert.Equal(HttpStatusCode.Conflict, healthy.StatusCode);

        // El escritor falla en segundo plano, cae al log de archivo y no tumba nada.
        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
    }

    // ------------------------------------------------------------------ 4. evento fallido

    [Fact]
    public async Task Failing_event_handler_is_stored_for_retry_and_the_operation_still_succeeds()
    {
        await using var host = TestHost();
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();

        var response = await client.PostAsync("/test-logs/event", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var traceId = response.Headers.GetValues("X-Trace-Id").Single();

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var row = await db.EventHandlerFailures.SingleAsync(e => e.TraceId == traceId);

        Assert.Equal(typeof(LogTestEvent).FullName, row.EventType);
        Assert.Equal(typeof(FailingLogTestEventHandler).FullName, row.HandlerType);
        Assert.Equal("pending", row.RetryStatus);
        Assert.Equal("System.InvalidOperationException", row.ExceptionType);

        var payload = JsonDocument.Parse(row.Payload).RootElement;
        Assert.Equal("evento", payload.GetProperty("name").GetString());
        Assert.Equal(LogSanitizer.Redacted, payload.GetProperty("password").GetString());
        Assert.DoesNotContain("s3cret-pass", row.Payload);
    }

    // ------------------------------------------------------------------ 5. tarea periódica, latido y purga

    [Fact]
    public async Task Job_run_records_a_row_and_updates_the_heartbeats()
    {
        await using var host = TestHost();
        host.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var client = host.CreateClient();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/jobs/communications/run");
        request.Headers.Add("X-Job-Secret", JobSecret);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);

        var run = await db.JobRuns.Where(r => r.Job == "communications").OrderByDescending(r => r.StartedAt).FirstAsync();
        Assert.True(run.StartedAt >= before);
        Assert.True(run.FinishedAt >= run.StartedAt);
        Assert.Contains(run.Status, new[] { "ok", "partial" });

        var beat = await db.Heartbeats.SingleAsync(h => h.Process == "communications_job");
        Assert.True(beat.LastBeatAt >= before);

        // La purga diaria corre dentro de la tarea: deja su propio latido de hoy.
        var purgeBeat = await db.Heartbeats.SingleAsync(h => h.Process == LogMaintenance.PurgeHeartbeat);
        Assert.True(purgeBeat.LastBeatAt >= DateTimeOffset.UtcNow.AddDays(-1));
    }

    [Fact]
    public async Task Purge_deletes_records_older_than_a_year_and_runs_once_per_day()
    {
        await using var host = TestHost();
        var maintenance = host.Services.GetRequiredService<LogMaintenance>();
        var now = DateTimeOffset.UtcNow;
        var old = now.AddDays(-800);
        var recent = now.AddDays(-5);
        var marker = $"purge-{Guid.NewGuid():N}";

        await maintenance.EnsureErrorPartitionAsync(old);
        await using (var db = OpenLogs(host))
        {
            db.Errors.AddRange(
                ErrorAt(old, marker), ErrorAt(recent, marker));
            db.JobRuns.AddRange(JobAt(old, marker), JobAt(recent, marker));
            db.EventHandlerFailures.AddRange(FailureAt(old, marker), FailureAt(recent, marker));
            await db.SaveChangesAsync();
            await db.Heartbeats.Where(h => h.Process == LogMaintenance.PurgeHeartbeat).ExecuteDeleteAsync();
        }

        var first = await maintenance.RunDailyIfDueAsync();
        var second = await maintenance.RunDailyIfDueAsync();

        Assert.NotNull(first);
        Assert.True(first!.PartitionsDropped >= 1);
        Assert.Null(second);                                       // ya corrió hoy

        await using var check = OpenLogs(host);
        Assert.Equal(1, await check.Errors.CountAsync(e => e.TraceId == marker));
        Assert.Equal(1, await check.JobRuns.CountAsync(e => e.Job == marker));
        Assert.Equal(1, await check.EventHandlerFailures.CountAsync(e => e.TraceId == marker));
        Assert.True(await check.Errors.AnyAsync(e => e.TraceId == marker && e.OccurredAt > now.AddDays(-30)));
    }

    // ------------------------------------------------------------------ utilidades puras

    [Fact]
    public void Sanitizer_redacts_sensitive_names_at_any_depth()
    {
        var clean = LogSanitizer.SanitizeJson(
            """{"name":"Ana","password":"x","nested":{"refreshToken":"t","medicalConditions":"asma"},"items":[{"inviteCode":"c","ok":1}]}""");

        var doc = JsonDocument.Parse(clean).RootElement;
        Assert.Equal("Ana", doc.GetProperty("name").GetString());
        Assert.Equal(LogSanitizer.Redacted, doc.GetProperty("password").GetString());
        Assert.Equal(LogSanitizer.Redacted, doc.GetProperty("nested").GetProperty("refreshToken").GetString());
        Assert.Equal(LogSanitizer.Redacted, doc.GetProperty("nested").GetProperty("medicalConditions").GetString());
        Assert.Equal(LogSanitizer.Redacted, doc.GetProperty("items")[0].GetProperty("inviteCode").GetString());
        Assert.Equal(1, doc.GetProperty("items")[0].GetProperty("ok").GetInt32());
    }

    [Fact]
    public void Sanitizer_drops_bodies_that_are_not_json_and_truncates_long_ones()
    {
        Assert.DoesNotContain("pass", LogSanitizer.SanitizeJson("password=pass&x=1"));
        var big = LogSanitizer.SanitizeBodyForSummary("{\"a\":\"" + new string('z', 10_000) + "\"}");
        Assert.True(big.Length < LogSanitizer.RequestSummaryMaxChars + 20);
    }

    [Fact]
    public void Trace_code_is_the_first_six_characters_in_upper_case()
        => Assert.Equal("7F3A2C", TraceCode.Short("7f3a2cdeadbeef"));

    private static ErrorRow ErrorAt(DateTimeOffset at, string marker) => new()
    {
        Id = Guid.CreateVersion7(), OccurredAt = at, TraceId = marker, Source = "api",
        ExceptionType = "T", Message = "m", Fingerprint = "f",
    };

    private static JobRunRow JobAt(DateTimeOffset at, string marker) => new()
    {
        Id = Guid.CreateVersion7(), OccurredAt = at, Job = marker, StartedAt = at, FinishedAt = at, Status = "ok",
    };

    private static EventHandlerFailureRow FailureAt(DateTimeOffset at, string marker) => new()
    {
        Id = Guid.CreateVersion7(), OccurredAt = at, TraceId = marker, EventType = "E", HandlerType = "H",
        Payload = "{}", ExceptionType = "T", Message = "m",
    };
}
