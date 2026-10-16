using GSM.Application.Common.Abstractions;
using GSM.Infrastructure.Email;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>I5-3: sin proveedor de correo, los códigos solo llegan al log en Development.</summary>
public sealed class LoggingEmailSenderTests
{
    private static readonly EmailMessage CodeEmail =
        new("ana@test.local", "Tu código", "<p>482913</p>", "Tu código de acceso: 482913", Sensitive: true);

    [Fact]
    public async Task In_development_the_code_is_written_to_the_log()
    {
        var logger = new ListLogger();
        await new LoggingEmailSender(logger, new Env(Environments.Development)).SendAsync(CodeEmail);

        Assert.Contains("482913", Assert.Single(logger.Lines));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_the_code_never_reaches_the_log(string environment)
    {
        var logger = new ListLogger();
        await new LoggingEmailSender(logger, new Env(environment)).SendAsync(CodeEmail);

        var line = Assert.Single(logger.Lines);
        Assert.DoesNotContain("482913", line);
        Assert.Contains("contenido omitido", line);
    }

    [Fact]
    public async Task A_non_sensitive_email_is_logged_in_any_environment()
    {
        var logger = new ListLogger();
        await new LoggingEmailSender(logger, new Env(Environments.Production))
            .SendAsync(new EmailMessage("ana@test.local", "Aviso", "<p>Hola</p>", "Hola Ana"));

        Assert.Contains("Hola Ana", Assert.Single(logger.Lines));
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "GSM.API";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ListLogger : ILogger<LoggingEmailSender>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception));
    }
}
