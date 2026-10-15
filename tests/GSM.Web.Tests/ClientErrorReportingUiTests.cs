using Bunit;
using GSM.Client.Generated;
using GSM.UI.Shared.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>LG3: el borde de errores muestra el código y reporta; el reporter no repite ni lanza.</summary>
public class ClientErrorReportingUiTests : TestContext
{
    private sealed class FakeLogsClient : ILogsClient
    {
        public List<ClientErrorRequest> Reports { get; } = [];

        public bool Fails { get; set; }

        public Task ReportClientErrorAsync(ClientErrorRequest body, CancellationToken cancellationToken = default)
        {
            if (Fails)
            {
                throw new HttpRequestException("sin red");
            }

            Reports.Add(body);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEnvironment : IClientEnvironment
    {
        public string? AppVersion => "1.2.3";

        public string? ResolvePlatform(string route) => route.StartsWith("/app") ? "web_member" : "web_panel";

        public Task<string?> GetDeviceInfoAsync() => Task.FromResult<string?>("Chrome test");
    }

    private sealed class Boom : ComponentBase
    {
        protected override void OnInitialized() => throw new InvalidOperationException("la pantalla falló");
    }

    private FakeLogsClient Register()
    {
        var logs = new FakeLogsClient();
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ILogsClient>(logs);
        Services.AddSingleton<IClientEnvironment, FakeEnvironment>();
        Services.AddScoped<ClientErrorReporter>();
        return logs;
    }

    [Fact]
    public async Task Boundary_shows_the_trace_code_and_reports_the_error_once()
    {
        var logs = Register();

        var cut = RenderComponent<GsmErrorBoundary>(p => p.AddChildContent<Boom>());

        var alert = cut.Find("[data-testid=error-boundary]");
        Assert.Contains("Algo salió mal · código ", alert.TextContent);
        await cut.WaitForAssertionAsync(() => Assert.Single(logs.Reports));
        var report = logs.Reports[0];
        Assert.Equal("System.InvalidOperationException", report.ExceptionType);
        Assert.Equal("web_panel", report.Platform);
        Assert.Equal("Chrome test", report.DeviceInfo);
        Assert.Equal("1.2.3", report.AppVersion);
        Assert.False(string.IsNullOrEmpty(report.TraceId));
        Assert.Contains(report.TraceId![..6].ToUpperInvariant(), alert.TextContent);
    }

    [Fact]
    public void Boundary_works_without_a_reporter_registered()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<GsmErrorBoundary>(p => p.AddChildContent<Boom>());

        Assert.Contains("Algo salió mal.", cut.Find("[data-testid=error-boundary]").TextContent);
    }

    [Fact]
    public async Task Reporter_does_not_repeat_the_same_error_and_never_throws_when_sending_fails()
    {
        var logs = Register();
        var reporter = Services.GetRequiredService<ClientErrorReporter>();

        var first = reporter.Report(new InvalidOperationException("igual"));
        var second = reporter.Report(new InvalidOperationException("igual"));

        Assert.Equal(first, second);
        await Task.Delay(100);
        Assert.Single(logs.Reports);

        logs.Fails = true;
        var failing = reporter.Report(new InvalidOperationException("otro"));
        Assert.NotNull(failing);                                    // devuelve el código aunque el envío falle
    }

    [Fact]
    public void Reporter_shows_the_server_trace_code_of_an_api_error_and_does_not_send_it_again()
    {
        var logs = Register();
        var reporter = Services.GetRequiredService<ClientErrorReporter>();
        var api = new GsmApiException(
            "500", 500, """{"status":500,"traceId":"7f3a2c11223344556677889900aabbcc"}""",
            new Dictionary<string, IEnumerable<string>>(), null);

        var code = reporter.Report(api);

        Assert.Equal("7F3A2C", code);
        Assert.Empty(logs.Reports);
    }
}
