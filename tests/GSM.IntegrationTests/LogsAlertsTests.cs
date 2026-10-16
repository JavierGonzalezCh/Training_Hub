using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Logs.Alerts;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Logs;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>LG5: alertas al SuperAdmin (docs/GSM-logs.md §8): detección, repetición, entrega y panel.</summary>
public sealed class LogsAlertsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AdminPassword = "AdminPass_123";
    private const string JobSecret = "integration-tests-jobs-secret-32-bytes-min!!";

    private sealed record World(Guid AdminId, string? AdminEmail, string PushToken);

    /// <summary>Un correo que falla las primeras <c>FailTimes</c> veces y luego funciona.</summary>
    private sealed class FlakyEmailSender(int failTimes) : IEmailSender
    {
        private int _calls;

        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) <= failTimes)
            {
                throw new InvalidOperationException("Resend caído");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    // ---------------------------------------------------------------- error nuevo

    [Fact]
    public async Task A_new_error_group_alerts_once_by_email_and_push_and_never_again()
    {
        var w = await SeedAsync();
        var suffix = Suffix();
        var type = "Lg5.NewBoom_" + suffix;
        var fp = "fp-lg5-" + suffix;
        using var scope = Scope();
        await AddErrorsAsync(scope, fp, type, 1, DateTimeOffset.UtcNow.AddMinutes(-1));

        var evaluator = scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>();
        await evaluator.RunOnceAsync();

        var alert = Assert.Single(await AlertsAsync(scope, $"new:error:{fp}"));
        Assert.Equal(PlatformAlertKinds.NewError, alert.Kind);
        Assert.Contains(type, alert.Title);
        Assert.Equal("sent", alert.EmailStatus);
        Assert.Equal("sent", alert.PushStatus);

        var email = factory.Services.GetRequiredService<CapturingEmailSender>();
        var push = factory.Services.GetRequiredService<CapturingPushSender>();
        Assert.Single(email.Sent, m => m.To == w.AdminEmail && m.Subject.Contains(type));
        Assert.Single(push.SentTo(w.PushToken), m => m.Title.Contains(type));

        // Otra vuelta no repite ni la alerta ni el envío: el error nuevo avisa una sola vez por grupo.
        await evaluator.RunOnceAsync();
        Assert.Single(await AlertsAsync(scope, $"new:error:{fp}"));
        Assert.Single(email.Sent, m => m.Subject.Contains(type));
        Assert.Single(push.SentTo(w.PushToken), m => m.Title.Contains(type));
    }

    [Fact]
    public async Task A_failed_event_handler_group_also_alerts_as_new()
    {
        await SeedAsync();
        var suffix = Suffix();
        var eventType = "GSM.Lg5.Events.Evt" + suffix;
        var handlerType = "GSM.Lg5.Handlers.Hnd" + suffix;
        using var scope = Scope();
        await using (var db = OpenLogs(scope))
        {
            db.EventHandlerFailures.Add(new EventHandlerFailureRow
            {
                Id = Guid.CreateVersion7(), OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                TraceId = Guid.NewGuid().ToString("N"), EventType = eventType, HandlerType = handlerType,
                Payload = "{}", ExceptionType = "System.InvalidOperationException", Message = "falló",
            });
            await db.SaveChangesAsync();
        }

        await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>().RunOnceAsync();

        var alert = Assert.Single(await AlertsAsync(scope, $"new:event_failure:{eventType}|{handlerType}"));
        Assert.Contains("Evt" + suffix, alert.Title);
        Assert.Contains("Hnd" + suffix, alert.Title);
    }

    [Fact]
    public async Task An_ignored_group_does_not_alert()
    {
        await SeedAsync();
        var suffix = Suffix();
        var fp = "fp-lg5-ign-" + suffix;
        using var scope = Scope();
        await AddErrorsAsync(scope, fp, "Lg5.Ignored_" + suffix, 1, DateTimeOffset.UtcNow.AddMinutes(-1));
        await using (var db = OpenLogs(scope))
        {
            db.ErrorGroups.Add(new ErrorGroupRow
            {
                Kind = "error", Fingerprint = fp, Title = "ignorado", FirstSeenAt = DateTimeOffset.UtcNow,
                Status = "ignored", StatusChangedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>().RunOnceAsync();

        Assert.Empty(await AlertsAsync(scope, $"new:error:{fp}"));
    }

    [Fact]
    public async Task A_group_seen_long_ago_does_not_alert_as_new()
    {
        await SeedAsync();
        var suffix = Suffix();
        var fp = "fp-lg5-old-" + suffix;
        using var scope = Scope();
        await AddErrorsAsync(scope, fp, "Lg5.Old_" + suffix, 1, DateTimeOffset.UtcNow.AddHours(-3));

        await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>().RunOnceAsync();

        Assert.Empty(await AlertsAsync(scope, $"new:error:{fp}"));
    }

    // ---------------------------------------------------------------- pico

    [Fact]
    public async Task A_spike_needs_more_than_20_occurrences_in_10_minutes()
    {
        await SeedAsync();
        var suffix = Suffix();
        var fpExact = "fp-lg5-20-" + suffix;
        var fpOver = "fp-lg5-21-" + suffix;
        using var scope = Scope();
        var at = DateTimeOffset.UtcNow.AddMinutes(-2);
        await AddErrorsAsync(scope, fpExact, "Lg5.Exact_" + suffix, 20, at);
        await AddErrorsAsync(scope, fpOver, "Lg5.Over_" + suffix, 21, at);

        await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>().RunOnceAsync();

        Assert.Empty(await AlertsAsync(scope, $"spike:error:{fpExact}"));
        var spike = Assert.Single(await AlertsAsync(scope, $"spike:error:{fpOver}"));
        Assert.Equal(PlatformAlertKinds.Spike, spike.Kind);
        Assert.Contains("21", spike.Body);
    }

    [Fact]
    public async Task The_same_spike_does_not_repeat_within_30_minutes_but_does_after()
    {
        await SeedAsync();
        var suffix = Suffix();
        var fp = "fp-lg5-rep-" + suffix;
        using var scope = Scope();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var t = DateTimeOffset.UtcNow;
        try
        {
            clock.PinTo(t, DateOnly.FromDateTime(t.UtcDateTime));
            await AddErrorsAsync(scope, fp, "Lg5.Rep_" + suffix, 21, t.AddMinutes(-1));
            var evaluator = scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>();
            await evaluator.RunOnceAsync();
            Assert.Single(await AlertsAsync(scope, $"spike:error:{fp}"));

            // Cinco minutos después el pico sigue dentro de la ventana, pero aún no pasaron los 30 minutos.
            clock.PinTo(t.AddMinutes(5), DateOnly.FromDateTime(t.UtcDateTime));
            await evaluator.RunOnceAsync();
            Assert.Single(await AlertsAsync(scope, $"spike:error:{fp}"));

            // A los 31 minutos, con un pico nuevo, vuelve a avisar.
            var later = t.AddMinutes(31);
            clock.PinTo(later, DateOnly.FromDateTime(later.UtcDateTime));
            await AddErrorsAsync(scope, fp, "Lg5.Rep_" + suffix, 21, later.AddMinutes(-1));
            await evaluator.RunOnceAsync();
            Assert.Equal(2, (await AlertsAsync(scope, $"spike:error:{fp}")).Count);
        }
        finally
        {
            clock.Unpin();
        }
    }

    // ---------------------------------------------------------------- proceso detenido

    [Fact]
    public async Task A_stale_job_heartbeat_alerts_but_a_fresh_one_or_a_skipped_one_does_not()
    {
        await SeedAsync();
        var suffix = Suffix();
        var stale = $"lg5{suffix}stale_job";
        var fresh = $"lg5{suffix}fresh_job";
        var skipped = $"lg5{suffix}skipped_job";
        var notAJob = $"lg5{suffix}other";
        using var scope = Scope();
        var now = DateTimeOffset.UtcNow;
        await SetHeartbeatsAsync(scope, (stale, now.AddMinutes(-31)), (fresh, now.AddMinutes(-10)),
            (skipped, now.AddMinutes(-90)), (notAJob, now.AddDays(-3)));
        try
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>().RunOnceAsync([skipped]);

            var alert = Assert.Single(await AlertsAsync(scope, $"heartbeat:{stale}"));
            Assert.Equal(PlatformAlertKinds.ProcessStopped, alert.Kind);
            Assert.Empty(await AlertsAsync(scope, $"heartbeat:{fresh}"));
            Assert.Empty(await AlertsAsync(scope, $"heartbeat:{skipped}"));
            Assert.Empty(await AlertsAsync(scope, $"heartbeat:{notAJob}"));
        }
        finally
        {
            // Latidos inventados: si se quedaran, cada evaluación futura los vería vencidos y volvería a avisar.
            await using var db = OpenLogs(scope);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM logs.heartbeats WHERE process LIKE {"lg5" + suffix + "%"}");
        }
    }

    [Fact]
    public async Task The_dispatcher_heartbeat_has_a_5_minute_deadline()
    {
        await SeedAsync();
        using var scope = Scope();
        const string key = "heartbeat:notifications_dispatcher";
        await ResetKeysAsync(scope, key);
        await SetHeartbeatsAsync(scope, ("notifications_dispatcher", DateTimeOffset.UtcNow.AddMinutes(-6)));
        var evaluator = scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>();

        await evaluator.RunOnceAsync(["notifications_dispatcher"]);
        Assert.Empty(await AlertsAsync(scope, key));

        await evaluator.RunOnceAsync();
        Assert.Single(await AlertsAsync(scope, key));

        // Que otra copia siga latiendo la deja al día.
        await SetHeartbeatsAsync(scope, ("notifications_dispatcher", DateTimeOffset.UtcNow));
    }

    // ---------------------------------------------------------------- servicio externo y seguridad

    [Fact]
    public async Task Five_failures_of_the_same_provider_alert_and_four_do_not()
    {
        await SeedAsync();
        var suffix = Suffix();
        var down = "lg5down" + suffix;
        var fine = "lg5fine" + suffix;
        using var scope = Scope();
        await using (var db = OpenLogs(scope))
        {
            for (var i = 0; i < 5; i++)
            {
                db.ExternalFailures.Add(ExternalFailure(down));
            }

            for (var i = 0; i < 4; i++)
            {
                db.ExternalFailures.Add(ExternalFailure(fine));
            }

            await db.SaveChangesAsync();
        }

        await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>().RunOnceAsync();

        var alert = Assert.Single(await AlertsAsync(scope, $"external:{down}"));
        Assert.Equal(PlatformAlertKinds.ExternalDown, alert.Kind);
        Assert.Empty(await AlertsAsync(scope, $"external:{fine}"));
    }

    [Fact]
    public async Task Twenty_failed_logins_from_one_ip_alert_and_nineteen_do_not()
    {
        await SeedAsync();
        var n = Random.Shared.Next(1, 200);
        var ipHot = $"203.0.113.{n}";
        var ipCold = $"198.51.100.{n}";
        using var scope = Scope();
        await using (var db = OpenLogs(scope))
        {
            for (var i = 0; i < 20; i++)
            {
                db.SecurityEvents.Add(LoginFailed(ipHot));
            }

            for (var i = 0; i < 19; i++)
            {
                db.SecurityEvents.Add(LoginFailed(ipCold));
            }

            await db.SaveChangesAsync();
        }

        await ResetKeysAsync(scope, $"security:login:{ipHot}", $"security:login:{ipCold}");
        await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>().RunOnceAsync();

        var alert = Assert.Single(await AlertsAsync(scope, $"security:login:{ipHot}"));
        Assert.Equal(PlatformAlertKinds.Security, alert.Kind);
        Assert.Contains(ipHot, alert.Body);
        Assert.Empty(await AlertsAsync(scope, $"security:login:{ipCold}"));
    }

    // ---------------------------------------------------------------- entrega

    [Fact]
    public async Task A_failed_email_is_retried_with_backoff_and_then_sent()
    {
        await SeedAsync(withPush: false);
        var suffix = Suffix();
        var type = "Lg5.Flaky_" + suffix;
        var fp = "fp-lg5-flaky-" + suffix;
        var flaky = new FlakyEmailSender(failTimes: 1);
        await using var host = HostWith(flaky);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var t = DateTimeOffset.UtcNow;
        try
        {
            clock.PinTo(t, DateOnly.FromDateTime(t.UtcDateTime));
            await AddErrorsAsync(scope, fp, type, 1, t.AddMinutes(-1));
            var evaluator = scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>();

            await evaluator.RunOnceAsync();
            var afterFirst = Assert.Single(await AlertsAsync(scope, $"new:error:{fp}"));
            Assert.Equal("pending", afterFirst.EmailStatus);
            Assert.Equal("skipped", afterFirst.PushStatus); // sin dispositivos
            Assert.Equal(1, afterFirst.Attempts);
            Assert.NotNull(afterFirst.LastError);

            // Antes de la espera no se reintenta.
            clock.PinTo(t.AddSeconds(30), DateOnly.FromDateTime(t.UtcDateTime));
            await evaluator.RunOnceAsync();
            Assert.Equal(1, (await AlertsAsync(scope, $"new:error:{fp}")).Single().Attempts);

            clock.PinTo(t.AddMinutes(3), DateOnly.FromDateTime(t.UtcDateTime));
            await evaluator.RunOnceAsync();
            var afterRetry = Assert.Single(await AlertsAsync(scope, $"new:error:{fp}"));
            Assert.Equal("sent", afterRetry.EmailStatus);
            Assert.Equal(2, afterRetry.Attempts);
            Assert.Single(flaky.Sent, m => m.Subject.Contains(type));
        }
        finally
        {
            clock.Unpin();
        }
    }

    [Fact]
    public async Task An_email_that_keeps_failing_ends_as_failed_after_three_attempts()
    {
        await SeedAsync(withPush: false);
        var suffix = Suffix();
        var fp = "fp-lg5-dead-" + suffix;
        await using var host = HostWith(new FlakyEmailSender(failTimes: 100));
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var t = DateTimeOffset.UtcNow;
        try
        {
            clock.PinTo(t, DateOnly.FromDateTime(t.UtcDateTime));
            await AddErrorsAsync(scope, fp, "Lg5.Dead_" + suffix, 1, t.AddMinutes(-1));
            var evaluator = scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>();

            await evaluator.RunOnceAsync();
            clock.PinTo(t.AddMinutes(3), DateOnly.FromDateTime(t.UtcDateTime));
            await evaluator.RunOnceAsync();
            clock.PinTo(t.AddMinutes(6), DateOnly.FromDateTime(t.UtcDateTime));
            await evaluator.RunOnceAsync();
            clock.PinTo(t.AddMinutes(9), DateOnly.FromDateTime(t.UtcDateTime));
            await evaluator.RunOnceAsync(); // un cuarto intento no existe

            var alert = Assert.Single(await AlertsAsync(scope, $"new:error:{fp}"));
            Assert.Equal("failed", alert.EmailStatus);
            Assert.Equal(3, alert.Attempts);
        }
        finally
        {
            clock.Unpin();
        }
    }

    [Fact]
    public async Task Without_a_superadmin_both_channels_are_skipped_and_nothing_is_retried()
    {
        await SeedAsync(withAdmin: false);
        var suffix = Suffix();
        var fp = "fp-lg5-noadmin-" + suffix;
        using var scope = Scope();
        await AddErrorsAsync(scope, fp, "Lg5.NoAdmin_" + suffix, 1, DateTimeOffset.UtcNow.AddMinutes(-1));

        await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>().RunOnceAsync();

        var alert = Assert.Single(await AlertsAsync(scope, $"new:error:{fp}"));
        Assert.Equal("skipped", alert.EmailStatus);
        Assert.Equal("skipped", alert.PushStatus);
    }

    [Fact]
    public async Task A_push_token_the_provider_rejects_revokes_the_device_and_does_not_fail_the_alert()
    {
        var w = await SeedAsync();
        var suffix = Suffix();
        var fp = "fp-lg5-badtoken-" + suffix;
        var push = factory.Services.GetRequiredService<CapturingPushSender>();
        push.InvalidTokens[w.PushToken] = true;
        try
        {
            using var scope = Scope();
            await AddErrorsAsync(scope, fp, "Lg5.BadToken_" + suffix, 1, DateTimeOffset.UtcNow.AddMinutes(-1));

            await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>().RunOnceAsync();

            var alert = Assert.Single(await AlertsAsync(scope, $"new:error:{fp}"));
            Assert.Equal("skipped", alert.PushStatus);
            Assert.Equal("sent", alert.EmailStatus);
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.NotNull((await db.Set<PushDevice>().IgnoreQueryFilters().SingleAsync(d => d.Token == w.PushToken)).RevokedAt);
        }
        finally
        {
            push.InvalidTokens.TryRemove(w.PushToken, out _);
        }
    }

    // ---------------------------------------------------------------- tarea periódica y panel

    [Fact]
    public async Task The_periodic_job_also_evaluates_the_alerts()
    {
        await SeedAsync();
        var suffix = Suffix();
        var fp = "fp-lg5-job-" + suffix;
        using (var scope = Scope())
        {
            await AddErrorsAsync(scope, fp, "Lg5.Job_" + suffix, 1, DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        factory.Services.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/jobs/communications/run");
        request.Headers.Add("X-Job-Secret", JobSecret);
        var response = await factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var check = Scope();
        Assert.Single(await AlertsAsync(check, $"new:error:{fp}"));
    }

    [Fact]
    public async Task Only_the_superadmin_lists_the_alerts_and_the_newest_come_first()
    {
        var w = await SeedAsync();
        var suffix = Suffix();
        var fp = "fp-lg5-panel-" + suffix;
        var type = "Lg5.Panel_" + suffix;
        using (var scope = Scope())
        {
            await AddErrorsAsync(scope, fp, type, 1, DateTimeOffset.UtcNow.AddMinutes(-1));
            await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>().RunOnceAsync();
        }

        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await host.CreateClient().GetAsync("/api/admin/logs/alerts")).StatusCode);

        var admin = host.CreateClient();
        var login = await admin.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.AdminEmail!, AdminPassword));
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.Tokens!.AccessToken);

        var page = await admin.GetFromJsonAsync<JsonElement>("/api/admin/logs/alerts?pageSize=100");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        var mine = Assert.Single(items, i => i.GetProperty("title").GetString()!.Contains(type));
        Assert.Equal("new_error", mine.GetProperty("kind").GetString());
        Assert.Equal("sent", mine.GetProperty("emailStatus").GetString());
        Assert.Equal("/admin/logs", mine.GetProperty("link").GetString());

        var createdAt = items.Select(i => i.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(createdAt.OrderByDescending(c => c), createdAt);
    }

    // ---------------------------------------------------------------- ayudas

    private static string Suffix() => Guid.NewGuid().ToString("N")[..10];

    private IServiceScope Scope()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();
        return scope;
    }

    private WebApplicationFactory<Program> HostWith(IEmailSender sender)
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton(sender);
        }));

    private static LogsDbContext OpenLogs(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDbContextFactory<LogsDbContext>>().CreateDbContext();

    private static async Task AddErrorsAsync(IServiceScope scope, string fingerprint, string type, int count, DateTimeOffset at)
    {
        await using var db = OpenLogs(scope);
        for (var i = 0; i < count; i++)
        {
            db.Errors.Add(new ErrorRow
            {
                Id = Guid.CreateVersion7(), OccurredAt = at, TraceId = Guid.NewGuid().ToString("N"),
                Source = "api", Method = "GET", Endpoint = "/api/lg5/{id}",
                ExceptionType = type, Message = "falla de prueba", Fingerprint = fingerprint,
            });
        }

        await db.SaveChangesAsync();
    }

    private static ExternalFailureRow ExternalFailure(string provider) => new()
    {
        Id = Guid.CreateVersion7(), OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        TraceId = Guid.NewGuid().ToString("N"), Provider = provider, Operation = "send", StatusCode = 503, DurationMs = 120,
    };

    private static SecurityEventRow LoginFailed(string ip) => new()
    {
        Id = Guid.CreateVersion7(), OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        TraceId = Guid.NewGuid().ToString("N"), EventType = "login_failed", Ip = ip,
    };

    private static async Task SetHeartbeatsAsync(IServiceScope scope, params (string Process, DateTimeOffset At)[] beats)
    {
        await using var db = OpenLogs(scope);
        foreach (var (process, at) in beats)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO logs.heartbeats (process, last_beat_at) VALUES ({process}, {at})
                 ON CONFLICT (process) DO UPDATE SET last_beat_at = EXCLUDED.last_beat_at
                 """);
        }
    }

    private static async Task ResetKeysAsync(IServiceScope scope, params string[] keys)
    {
        await using var db = OpenLogs(scope);
        foreach (var key in keys)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM logs.alert_state WHERE key = {key}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM logs.platform_alerts WHERE key = {key}");
        }
    }

    private static async Task<List<PlatformAlertRow>> AlertsAsync(IServiceScope scope, string key)
    {
        await using var db = OpenLogs(scope);
        return await db.PlatformAlerts.AsNoTracking().Where(a => a.Key == key).OrderBy(a => a.CreatedAt).ToListAsync();
    }

    /// <summary>Deja un SuperAdmin (con un dispositivo push) como único usuario; sin él si <paramref name="withAdmin"/> es falso.</summary>
    private async Task<World> SeedAsync(bool withAdmin = true, bool withPush = true)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, notifications.push_devices restart identity cascade;");

        factory.Services.GetRequiredService<CapturingEmailSender>().Clear();
        factory.Services.GetRequiredService<CapturingPushSender>().Clear();
        if (!withAdmin)
        {
            return new World(Guid.Empty, null, string.Empty);
        }

        var email = $"sa-lg5-{Suffix()}@test.local";
        var adminId = await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
            .CreateSuperAdminAsync(email, "Super Admin", AdminPassword);

        var token = "lg5-token-" + Suffix();
        if (withPush)
        {
            db.Add(PushDevice.Register(adminId, token, PushPlatform.WebPanel, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        return new World(adminId, email, token);
    }
}
