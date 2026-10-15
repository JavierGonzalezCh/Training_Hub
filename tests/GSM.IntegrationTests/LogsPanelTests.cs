using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Logs;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

// ---- Artefactos de prueba del reintento: eventos propios con un manejador que cuenta y puede fallar. ----

public sealed record LogRetryTestEvent(Guid Id, string Name) : IDomainEvent;

public sealed record LogRetryGroupTestEvent(Guid Id, string Name) : IDomainEvent;

public static class RetryTestCounter
{
    public static readonly ConcurrentDictionary<Guid, int> Executions = new();
}

public sealed class RetryTestHandler : IDomainEventHandler<LogRetryTestEvent>
{
    public Task HandleAsync(LogRetryTestEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent.Name == "fail")
        {
            throw new InvalidOperationException("sigue fallando");
        }

        RetryTestCounter.Executions.AddOrUpdate(domainEvent.Id, 1, (_, n) => n + 1);
        return Task.CompletedTask;
    }
}

public sealed class RetryGroupTestHandler : IDomainEventHandler<LogRetryGroupTestEvent>
{
    public Task HandleAsync(LogRetryGroupTestEvent domainEvent, CancellationToken cancellationToken = default)
    {
        RetryTestCounter.Executions.AddOrUpdate(domainEvent.Id, 1, (_, n) => n + 1);
        return Task.CompletedTask;
    }
}

/// <summary>LG4: panel de errores del SuperAdmin (agrupación, estados, búsqueda por código y reintento de eventos).</summary>
public sealed class LogsPanelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AdminPassword = "AdminPass_123";
    private const string OwnerPassword = "OwnerPass_123";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------------------------------------------------------------- agrupación y estados

    [Fact]
    public async Task Errors_with_the_same_fingerprint_are_grouped_with_count_and_affected_gyms()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var fp = "fp-" + Guid.NewGuid().ToString("N");
        var gymA = Guid.NewGuid();
        var gymB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await AddAsync(host,
            Error(fp, now.AddMinutes(-3), gymA), Error(fp, now.AddMinutes(-2), gymA), Error(fp, now.AddMinutes(-1), gymB),
            Error("fp-other-" + Guid.NewGuid().ToString("N"), now, gymA));

        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);
        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/logs/groups?kind=error&search={fp}");

        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(fp, item.GetProperty("fingerprint").GetString());
        Assert.Equal(3, item.GetProperty("count").GetInt32());
        Assert.Equal(2, item.GetProperty("gymsAffected").GetInt32());
        Assert.Equal("new", item.GetProperty("status").GetString());
        Assert.False(item.GetProperty("reopened").GetBoolean());

        var detail = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/admin/logs/groups/detail?kind=error&fingerprint={fp}");
        Assert.Equal(3, detail.GetProperty("occurrences").GetArrayLength());
    }

    [Fact]
    public async Task A_group_status_can_be_changed_and_a_resolved_group_that_recurs_reopens_itself()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var fp = "fp-" + Guid.NewGuid().ToString("N");
        await AddAsync(host, Error(fp, DateTimeOffset.UtcNow.AddMinutes(-10), null));
        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);

        var investigating = await admin.PutAsJsonAsync("/api/admin/logs/groups/status",
            new { kind = "error", fingerprint = fp, status = "investigating", notes = "mirando el endpoint" });
        Assert.Equal(HttpStatusCode.OK, investigating.StatusCode);
        var body = await investigating.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("investigating", body.GetProperty("status").GetString());
        Assert.Equal("mirando el endpoint", body.GetProperty("notes").GetString());

        // Sin notas en la petición se conservan las anteriores.
        var resolved = await admin.PutAsJsonAsync("/api/admin/logs/groups/status",
            new { kind = "error", fingerprint = fp, status = "resolved" });
        var resolvedBody = await resolved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("resolved", resolvedBody.GetProperty("status").GetString());
        Assert.Equal("mirando el endpoint", resolvedBody.GetProperty("notes").GetString());

        var listed = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/admin/logs/groups?kind=error&status=resolved&search={fp}");
        Assert.Equal(1, listed.GetProperty("items").GetArrayLength());

        // Vuelve a ocurrir: se reabre sola.
        await AddAsync(host, Error(fp, DateTimeOffset.UtcNow.AddMinutes(5), null));
        var reopened = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/logs/groups?kind=error&search={fp}");
        var item = Assert.Single(reopened.GetProperty("items").EnumerateArray());
        Assert.Equal("new", item.GetProperty("status").GetString());
        Assert.True(item.GetProperty("reopened").GetBoolean());
        Assert.Equal(2, item.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Invalid_status_is_422_and_an_unknown_group_is_404()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var fp = "fp-" + Guid.NewGuid().ToString("N");
        await AddAsync(host, Error(fp, DateTimeOffset.UtcNow, null));
        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync("/api/admin/logs/groups/status",
            new { kind = "error", fingerprint = fp, status = "banana" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/admin/logs/groups/status",
            new { kind = "error", fingerprint = "no-existe-" + fp, status = "resolved" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.GetAsync("/api/admin/logs/groups?status=banana")).StatusCode);
    }

    // ---------------------------------------------------------------- búsqueda por código

    [Fact]
    public async Task Trace_search_is_by_prefix_case_insensitive_and_can_return_several_requests_from_several_tables()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var prefix = Guid.NewGuid().ToString("N")[..6];
        var traceA = prefix + Guid.NewGuid().ToString("N")[..26];
        var traceB = prefix + Guid.NewGuid().ToString("N")[..26];
        var now = DateTimeOffset.UtcNow;

        var errorA = Error("fp-" + traceA, now.AddSeconds(-5), null);
        errorA.TraceId = traceA;
        var rejectionA = new BusinessRejectionRow
        {
            Id = Guid.CreateVersion7(), OccurredAt = now.AddSeconds(-4), TraceId = traceA, StatusCode = 409,
            ExceptionType = "ConflictException", Title = "Cupo lleno",
        };
        var failureB = new EventHandlerFailureRow
        {
            Id = Guid.CreateVersion7(), OccurredAt = now, TraceId = traceB, EventType = "X.Evt", HandlerType = "X.Hnd",
            ExceptionType = "Boom", Message = "falló",
        };
        await AddAsync(host, errorA, rejectionA, failureB);

        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);
        var result = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/logs/trace/{prefix.ToUpperInvariant()}");

        var records = result.GetProperty("records").EnumerateArray().ToList();
        Assert.Equal(3, records.Count);
        Assert.Equal(2, records.Select(r => r.GetProperty("traceId").GetString()).Distinct().Count());
        Assert.Contains(records, r => r.GetProperty("source").GetString() == "business_rejections"
            && r.GetProperty("statusCode").GetInt32() == 409);
        Assert.Contains(records, r => r.GetProperty("source").GetString() == "event_handler_failures");
        Assert.All(records, r => Assert.Equal(prefix.ToUpperInvariant(), r.GetProperty("traceCode").GetString()));

        // Un prefijo más largo acota a una sola petición.
        var narrowed = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/logs/trace/{traceA[..12]}");
        Assert.Equal(2, narrowed.GetProperty("records").GetArrayLength());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/admin/logs/trace/ab")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/admin/logs/trace/zzzzzz")).StatusCode);
    }

    // ---------------------------------------------------------------- reintento de eventos

    [Fact]
    public async Task Retrying_a_failed_event_runs_the_handler_marks_it_resolved_and_is_idempotent()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var eventId = Guid.NewGuid();
        var failure = EventFailure<LogRetryTestEvent, RetryTestHandler>(new { id = eventId, name = "ok" });
        await AddAsync(host, failure);
        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);

        var first = await admin.PostAsync($"/api/admin/logs/event-failures/{failure.Id}/retry", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("succeeded", firstBody.GetProperty("outcome").GetString());
        Assert.Equal("retried_ok", firstBody.GetProperty("retryStatus").GetString());
        Assert.Equal(1, RetryTestCounter.Executions[eventId]);

        await using (var db = OpenLogs(host))
        {
            var row = await db.EventHandlerFailures.AsNoTracking().SingleAsync(e => e.Id == failure.Id);
            Assert.Equal("retried_ok", row.RetryStatus);
            Assert.Equal(w.AdminId, row.RetriedBy);
            Assert.NotNull(row.RetriedAt);
        }

        // Segundo reintento: no ejecuta nada.
        var second = await (await admin.PostAsync($"/api/admin/logs/event-failures/{failure.Id}/retry", null))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("already_resolved", second.GetProperty("outcome").GetString());
        Assert.Equal(1, RetryTestCounter.Executions[eventId]);

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsync($"/api/admin/logs/event-failures/{Guid.NewGuid()}/retry", null)).StatusCode);
    }

    [Fact]
    public async Task A_handler_that_fails_again_leaves_the_event_as_retried_failed_and_it_can_be_retried_later()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var failure = EventFailure<LogRetryTestEvent, RetryTestHandler>(new { id = Guid.NewGuid(), name = "fail" });
        await AddAsync(host, failure);
        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);

        var result = await (await admin.PostAsync($"/api/admin/logs/event-failures/{failure.Id}/retry", null))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("failed", result.GetProperty("outcome").GetString());
        Assert.Equal("retried_failed", result.GetProperty("retryStatus").GetString());
        Assert.Contains("sigue fallando", result.GetProperty("message").GetString());

        var listed = await admin.GetFromJsonAsync<JsonElement>(
            "/api/admin/logs/event-failures?status=retried_failed&pageSize=100");
        Assert.Contains(listed.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == failure.Id);
    }

    [Fact]
    public async Task An_event_with_redacted_data_and_no_refresher_is_not_retryable_and_stays_pending()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var failure = new EventHandlerFailureRow
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = DateTimeOffset.UtcNow,
            EventType = "GSM.Application.Members.Events.MemberInvited",
            HandlerType = "GSM.Application.Members.SendInvitationEmailHandler",
            Payload = JsonSerializer.Serialize(new
            {
                memberId = Guid.NewGuid(), gymId = Guid.NewGuid(), email = "[email]", code = "[REDACTED]",
            }),
            ExceptionType = "Boom",
            Message = "el correo falló",
        };
        await AddAsync(host, failure);
        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);

        var result = await (await admin.PostAsync($"/api/admin/logs/event-failures/{failure.Id}/retry", null))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("not_retryable", result.GetProperty("outcome").GetString());
        Assert.Equal("pending", result.GetProperty("retryStatus").GetString());
    }

    [Fact]
    public async Task Retrying_a_group_runs_every_retryable_event_and_counts_the_rest()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var good1 = EventFailure<LogRetryGroupTestEvent, RetryGroupTestHandler>(new { id = idA, name = "a" });
        var good2 = EventFailure<LogRetryGroupTestEvent, RetryGroupTestHandler>(new { id = idB, name = "b" });
        var truncated = EventFailure<LogRetryGroupTestEvent, RetryGroupTestHandler>(new { _truncated = true });
        await AddAsync(host, good1, good2, truncated);
        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);

        var response = await admin.PostAsJsonAsync("/api/admin/logs/event-failures/retry-group",
            new { eventType = good1.EventType, handlerType = good1.HandlerType });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, body.GetProperty("notRetryable").GetInt32());
        Assert.Equal(1, RetryTestCounter.Executions[idA]);
        Assert.Equal(1, RetryTestCounter.Executions[idB]);

        // Repetir el grupo ya no ejecuta los resueltos.
        await admin.PostAsJsonAsync("/api/admin/logs/event-failures/retry-group",
            new { eventType = good1.EventType, handlerType = good1.HandlerType });
        Assert.Equal(1, RetryTestCounter.Executions[idA]);
    }

    [Fact]
    public async Task An_event_can_be_ignored_but_not_after_it_was_resolved()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var failure = EventFailure<LogRetryTestEvent, RetryTestHandler>(new { id = Guid.NewGuid(), name = "x" });
        await AddAsync(host, failure);
        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);

        var ignored = await admin.PostAsync($"/api/admin/logs/event-failures/{failure.Id}/ignore", null);
        Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);
        var retry = await (await admin.PostAsync($"/api/admin/logs/event-failures/{failure.Id}/retry", null))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("already_resolved", retry.GetProperty("outcome").GetString());
        Assert.Equal("ignored", retry.GetProperty("retryStatus").GetString());
    }

    // ---------------------------------------------------------------- acceso

    [Fact]
    public async Task Only_the_superadmin_with_their_own_session_reaches_the_panel()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.CreateClient().GetAsync("/api/admin/logs/groups")).StatusCode);

        var owner = await LoggedInAsync(host, w.OwnerEmail, OwnerPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/logs/groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/logs/trace/abcdef")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.PostAsync($"/api/admin/logs/event-failures/{Guid.NewGuid()}/retry", null)).StatusCode);

        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);
        foreach (var mode in new[] { "read", "write" })
        {
            var opened = await admin.PostAsJsonAsync($"/api/admin/impersonate/{w.GymId}", new { reason = "Soporte", mode });
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            var token = (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
            var impersonating = host.CreateClient();
            impersonating.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Assert.Equal(HttpStatusCode.Forbidden, (await impersonating.GetAsync("/api/admin/logs/groups")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await impersonating.PostAsJsonAsync("/api/admin/logs/event-failures/retry-group",
                    new { eventType = "a", handlerType = "b" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/logs/groups")).StatusCode);
    }

    // ---------------------------------------------------------------- helpers

    private static ErrorRow Error(string fingerprint, DateTimeOffset at, Guid? gymId) => new()
    {
        Id = Guid.CreateVersion7(),
        OccurredAt = at,
        TraceId = Guid.NewGuid().ToString("N"),
        GymId = gymId,
        Source = "api",
        Method = "GET",
        Endpoint = "/api/test/{id}",
        ExceptionType = "System.InvalidOperationException",
        Message = "falla de prueba",
        Fingerprint = fingerprint,
    };

    private static EventHandlerFailureRow EventFailure<TEvent, THandler>(object payload) => new()
    {
        Id = Guid.CreateVersion7(),
        OccurredAt = DateTimeOffset.UtcNow,
        TraceId = Guid.NewGuid().ToString("N"),
        EventType = typeof(TEvent).FullName!,
        HandlerType = typeof(THandler).FullName!,
        Payload = JsonSerializer.Serialize(payload, Json),
        ExceptionType = "System.InvalidOperationException",
        Message = "falló",
    };

    private static LogsDbContext OpenLogs(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<IDbContextFactory<LogsDbContext>>().CreateDbContext();

    private static async Task AddAsync(WebApplicationFactory<Program> host, params object[] rows)
    {
        await using var db = OpenLogs(host);
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
            services.AddScoped<IDomainEventHandler<LogRetryTestEvent>, RetryTestHandler>();
            services.AddScoped<IDomainEventHandler<LogRetryGroupTestEvent>, RetryGroupTestHandler>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email, string password)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.Tokens!.AccessToken);
        return client;
    }

    private sealed record World(Guid AdminId, string AdminEmail, Guid GymId, string OwnerEmail);

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, " +
            "payments.payments, notifications.notifications, " +
            "superadmin.gym_subscriptions, superadmin.impersonation_sessions restart identity cascade;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"sa-{suffix}@test.local";
        var ownerEmail = $"own-{suffix}@test.local";
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var adminId = await provisioning.CreateSuperAdminAsync(adminEmail, "Super Admin", AdminPassword);

        var gym = Gym.Create("LG4 Gym", $"lg4-{suffix}");
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Dueña LG4", OwnerPassword, UserRole.Owner, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(adminId, adminEmail, gym.Id, ownerEmail);
    }
}
