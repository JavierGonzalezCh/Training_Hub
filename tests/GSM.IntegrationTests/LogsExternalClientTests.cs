using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GSM.Application.Common.Logs;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Email;
using GSM.Infrastructure.Logs;
using GSM.Infrastructure.Notifications.Push;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// LG3: <c>external_failures</c> (Resend, R2, Firebase) y <c>client_errors</c> (docs/GSM-logs.md §4.5, §4.6).
/// La prueba de purga y particiones de estas dos tablas vive en <see cref="LogsTests"/>, junto a las demás.
/// R2 no tiene prueba propia: el cliente S3 se crea dentro del constructor y no hay costura para simular su
/// fallo sin red; usa el mismo <c>RecordExternalFailure</c> que los otros dos.
/// </summary>
public sealed class LogsExternalClientTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private static LogsDbContext OpenLogs(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<IDbContextFactory<LogsDbContext>>().CreateDbContext();

    // ------------------------------------------------------------------ servicios externos

    [Fact]
    public async Task Resend_failure_leaves_a_row_without_secrets_and_the_send_does_not_throw()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        var logStore = host.Services.GetRequiredService<ILogStore>();
        var marker = $"resend-{Guid.NewGuid():N}";
        const string apiKey = "re_SuperSecretApiKey123";
        var sender = new ResendEmailSender(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    $"{marker} Authorization: Bearer {apiKey} rejected for ana@example.com token=abc123"),
            })),
            Options.Create(new ResendOptions { ApiKey = apiKey, FromAddress = "GSM <no-reply@example.com>" }),
            NullLogger<ResendEmailSender>.Instance,
            logStore);

        await sender.SendAsync(new GSM.Application.Common.Abstractions.EmailMessage("ana@example.com", "Hola", "<p>x</p>"));
        await logStore.FlushAsync();

        await using var db = OpenLogs(host);
        var row = await db.ExternalFailures.SingleAsync(f => f.ResponseBody!.Contains(marker));
        Assert.Equal("resend", row.Provider);
        Assert.Equal("send_email", row.Operation);
        Assert.Equal(401, row.StatusCode);
        Assert.Equal(1, row.Attempt);
        Assert.DoesNotContain(apiKey, row.ResponseBody);
        Assert.DoesNotContain("ana@example.com", row.ResponseBody);
        Assert.DoesNotContain("abc123", row.ResponseBody);
    }

    [Fact]
    public async Task Resend_network_error_is_recorded_without_a_status_code()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        var logStore = host.Services.GetRequiredService<ILogStore>();
        var marker = $"net-{Guid.NewGuid():N}";
        var sender = new ResendEmailSender(
            new HttpClient(new StubHandler(_ => throw new HttpRequestException(marker))),
            Options.Create(new ResendOptions { ApiKey = "k", FromAddress = "a@b.co" }),
            NullLogger<ResendEmailSender>.Instance,
            logStore);

        await sender.SendAsync(new GSM.Application.Common.Abstractions.EmailMessage("x@example.com", "Hola", "<p>x</p>"));
        await logStore.FlushAsync();

        await using var db = OpenLogs(host);
        var row = await db.ExternalFailures.SingleAsync(f => f.ResponseBody!.Contains(marker));
        Assert.Null(row.StatusCode);
        Assert.Equal("resend", row.Provider);
    }

    [Fact]
    public async Task Fcm_failure_is_recorded_and_still_throws_so_the_dispatcher_retries()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        var logStore = host.Services.GetRequiredService<ILogStore>();
        var marker = $"fcm-{Guid.NewGuid():N}";
        using var rsa = RSA.Create(2048);
        var account = JsonSerializer.Serialize(new
        {
            client_email = "svc@test.iam.gserviceaccount.com",
            private_key = rsa.ExportPkcs8PrivateKeyPem(),
            token_uri = "https://oauth.test/token",
        });
        var handler = new StubHandler(request => request.RequestUri!.Host == "oauth.test"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"ya29.fake","expires_in":3600}""", Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent($"{marker} UNAVAILABLE Authorization: Bearer ya29.fake"),
            });
        var sender = new FcmPushSender(
            new HttpClient(handler),
            Options.Create(new FcmOptions { ProjectId = "test-project", ServiceAccountJson = account }),
            NullLogger<FcmPushSender>.Instance,
            logStore);

        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(
            new GSM.Application.Notifications.Abstractions.PushMessage(
                "device-token", PushPlatform.Android, "t", "b", null, Guid.NewGuid(), Guid.NewGuid())));
        await logStore.FlushAsync();

        await using var db = OpenLogs(host);
        var row = await db.ExternalFailures.SingleAsync(f => f.ResponseBody!.Contains(marker));
        Assert.Equal("fcm", row.Provider);
        Assert.Equal("send", row.Operation);
        Assert.Equal(503, row.StatusCode);
        Assert.DoesNotContain("ya29.fake", row.ResponseBody);
    }

    // ------------------------------------------------------------------ errores del cliente

    private static object ClientError(string marker, string? traceId = null, string? stackTrace = null) => new
    {
        platform = "web_member",
        exceptionType = "System.InvalidOperationException",
        message = $"fallo {marker} con token=abc123",
        route = "/app/registro?code=SECRETO",
        stackTrace = stackTrace ?? "at Foo.Bar()",
        deviceInfo = "Chrome 130 / Android 15",
        traceId,
        appVersion = "9.9.9",
    };

    [Fact]
    public async Task Client_error_without_a_session_is_stored_with_the_trace_id_it_sent_and_clean_data()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        var client = host.CreateClient();
        var marker = $"cli-{Guid.NewGuid():N}";
        var traceId = Guid.NewGuid().ToString("N");

        var response = await client.PostAsJsonAsync("/api/logs/client", ClientError(marker, traceId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        var row = await db.ClientErrors.SingleAsync(e => e.TraceId == traceId);
        Assert.Equal("web_member", row.Platform);
        Assert.Equal("/app/registro", row.Route);                  // sin la consulta, que puede llevar códigos
        Assert.Equal("System.InvalidOperationException", row.ExceptionType);
        Assert.Contains(marker, row.Message);
        Assert.DoesNotContain("abc123", row.Message);
        Assert.Equal("Chrome 130 / Android 15", row.DeviceInfo);
        Assert.Equal("9.9.9", row.AppVersion);
    }

    [Fact]
    public async Task Client_error_without_a_trace_id_keeps_the_one_of_the_report_request()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        var client = host.CreateClient();

        var response = await client.PostAsJsonAsync("/api/logs/client", ClientError($"cli-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var requestTrace = response.Headers.GetValues("X-Trace-Id").Single();
        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        Assert.Equal(1, await db.ClientErrors.CountAsync(e => e.TraceId == requestTrace));
    }

    [Fact]
    public async Task Client_error_with_an_unknown_platform_or_an_oversized_field_is_a_400()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        var client = host.CreateClient();

        var badPlatform = await client.PostAsJsonAsync("/api/logs/client", new
        {
            platform = "playstation",
            exceptionType = "X",
            message = "m",
        });
        var longStack = await client.PostAsJsonAsync(
            "/api/logs/client", ClientError("x", stackTrace: new string('s', ClientErrorLimits.StackTrace + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, badPlatform.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longStack.StatusCode);
    }

    [Fact]
    public async Task Client_error_with_a_body_over_the_limit_is_rejected_with_413_and_not_stored()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        var client = host.CreateClient();
        var marker = $"big-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(
            "/api/logs/client",
            ClientError(marker, stackTrace: new string('s', ClientErrorLimits.MaxBodyBytes + 1_000)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var db = OpenLogs(host);
        Assert.False(await db.ClientErrors.AnyAsync(e => e.Message.Contains(marker)));
    }

    [Fact]
    public async Task Client_error_endpoint_is_rate_limited_per_ip()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        var client = host.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 25; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/logs/client", ClientError($"rl-{i}"))).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(HttpStatusCode.NoContent, statuses[0]);
    }
}
