using Microsoft.AspNetCore.Components;
using Bunit;
using GSM.Client.Generated;
using GSM.Web.Pages.Admin.Logs;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>LG4: panel de errores del SuperAdmin (grupos, estados, búsqueda por código y reintento de eventos).</summary>
public class LogsPanelUiTests : TestContext
{
    private sealed class FakeLogsClient : IAdminLogsClient
    {
        public List<ErrorGroupItem> Groups { get; } = [];
        public List<ErrorOccurrenceItem> Occurrences { get; } = [];
        public List<EventFailureItem> Events { get; } = [];
        public List<TraceRecordItem> TraceRecords { get; } = [];

        public string? LastKindFilter { get; private set; }
        public string? LastStatusFilter { get; private set; }
        public string? LastSort { get; private set; }
        public SetErrorGroupStatusRequest? StatusSet { get; private set; }
        public string? SearchedCode { get; private set; }
        public List<Guid> Retried { get; } = [];
        public RetryEventGroupRequest? GroupRetried { get; private set; }
        public string RetryOutcome { get; set; } = "succeeded";

        public Task<PagedResultOfErrorGroupItem> ListErrorGroupsAsync(
            string? kind = null, string? status = null, string? sort = null, string? search = null,
            int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
        {
            LastKindFilter = kind;
            LastStatusFilter = status;
            LastSort = sort;
            return Task.FromResult(new PagedResultOfErrorGroupItem
            {
                Items = Groups.Where(g => status is null || g.Status == status).ToList(),
                Page = 1, PageSize = 100, Total = Groups.Count,
            });
        }

        public Task<ErrorGroupDetail> GetErrorGroupAsync(
            string kind, string fingerprint, CancellationToken cancellationToken = default)
            => Task.FromResult(new ErrorGroupDetail
            {
                Group = Groups.Single(g => g.Kind == kind && g.Fingerprint == fingerprint),
                Occurrences = Occurrences.ToList(),
            });

        public Task<ErrorGroupItem> SetErrorGroupStatusAsync(
            SetErrorGroupStatusRequest body, CancellationToken cancellationToken = default)
        {
            StatusSet = body;
            var group = Groups.Single(g => g.Kind == body.Kind && g.Fingerprint == body.Fingerprint);
            group.Status = body.Status;
            group.Notes = body.Notes;
            group.Reopened = false;
            return Task.FromResult(group);
        }

        public Task<TraceSearchResponse> SearchLogsByTraceAsync(string code, CancellationToken cancellationToken = default)
        {
            SearchedCode = code;
            return Task.FromResult(new TraceSearchResponse { Query = code.ToUpperInvariant(), Records = TraceRecords.ToList() });
        }

        public Task<PagedResultOfEventFailureItem> ListEventFailuresAsync(
            string? status = null, string? eventType = null, string? handlerType = null,
            int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResultOfEventFailureItem
            {
                Items = Events.Where(e => status is null || e.RetryStatus == status).ToList(),
                Page = 1, PageSize = 100, Total = Events.Count,
            });

        public Task<RetryEventFailureResponse> RetryEventFailureAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Retried.Add(id);
            var item = Events.Single(e => e.Id == id);
            if (RetryOutcome == "succeeded")
            {
                item.RetryStatus = "retried_ok";
            }

            return Task.FromResult(new RetryEventFailureResponse
            {
                Id = id, Outcome = RetryOutcome, RetryStatus = item.RetryStatus,
                Message = RetryOutcome == "not_retryable" ? "El evento lleva datos que el log no guarda" : null,
            });
        }

        public Task<RetryEventGroupResponse> RetryEventFailureGroupAsync(
            RetryEventGroupRequest body, CancellationToken cancellationToken = default)
        {
            GroupRetried = body;
            return Task.FromResult(new RetryEventGroupResponse { Total = 2, Succeeded = 2 });
        }

        public List<PlatformAlertItem> Alerts { get; } = [];

        public Task<PagedResultOfPlatformAlertItem> ListPlatformAlertsAsync(
            int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResultOfPlatformAlertItem
            {
                Items = Alerts.ToList(), Page = 1, PageSize = 100, Total = Alerts.Count,
            });

        public Task<RetryEventFailureResponse> IgnoreEventFailureAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Events.Single(e => e.Id == id).RetryStatus = "ignored";
            return Task.FromResult(new RetryEventFailureResponse { Id = id, Outcome = "ignored", RetryStatus = "ignored" });
        }
    }

    private FakeLogsClient Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var client = new FakeLogsClient();
        Services.AddSingleton<IAdminLogsClient>(client);
        return client;
    }

    private static ErrorGroupItem Group(string kind, string fingerprint, string status = "new", bool reopened = false, int count = 3) => new()
    {
        Kind = kind, Fingerprint = fingerprint, Title = "System.InvalidOperationException", Detail = "/api/members/{id}",
        Count = count, GymsAffected = 2, FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-2), LastSeenAt = DateTimeOffset.UtcNow,
        Status = status, Reopened = reopened, PendingRetries = kind == "event_failure" ? 2 : 0,
    };

    private static EventFailureItem Failure(string status = "pending") => new()
    {
        Id = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, TraceId = "7f3a2c000000", TraceCode = "7F3A2C",
        EventType = "GSM.Application.Gyms.Events.GymCreated", HandlerType = "GSM.Application.SuperAdmin.CreateSubscriptionOnGymCreated",
        ExceptionType = "Boom", Message = "no se pudo crear", RetryStatus = status,
    };

    [Fact]
    public void Groups_list_shows_status_count_and_the_reopened_mark()
    {
        var client = Setup();
        client.Groups.Add(Group("error", "fp1", count: 7));
        client.Groups.Add(Group("error", "fp2", status: "new", reopened: true));
        client.Groups.Add(Group("error", "fp3", status: "resolved"));

        var cut = RenderComponent<ErrorGroupsPanel>();

        var rows = cut.FindAll("[data-testid='group-row']");
        Assert.Equal(3, rows.Count);
        Assert.Contains("7", rows[0].TextContent);
        Assert.Contains("Nuevo", rows[0].TextContent);
        Assert.Single(cut.FindAll("[data-testid='reopened']"));
        Assert.Contains("Resuelto", rows[2].TextContent);
    }

    [Fact]
    public void Filters_are_sent_to_the_api()
    {
        var client = Setup();
        client.Groups.Add(Group("error", "fp1"));
        client.Groups.Add(Group("error", "fp2", status: "resolved"));
        var cut = RenderComponent<ErrorGroupsPanel>();

        cut.Find("[data-testid='filter-status']").Change("resolved");
        cut.Find("[data-testid='filter-sort']").Change("frequent");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='group-row']")));
        Assert.Equal("resolved", client.LastStatusFilter);
        Assert.Equal("frequent", client.LastSort);
    }

    [Fact]
    public void Group_detail_lists_occurrences_and_changes_the_status_with_notes()
    {
        var client = Setup();
        client.Groups.Add(Group("error", "fp1"));
        client.Occurrences.Add(new ErrorOccurrenceItem
        {
            Id = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, TraceId = "aabbcc112233", TraceCode = "AABBCC",
            Method = "GET", Path = "/api/members/1", Message = "falla de prueba", StackTrace = "at X.Y()",
        });

        Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/logs/group?kind=error&fingerprint=fp1");
        var cut = RenderComponent<GroupDetail>();

        Assert.Equal(1, cut.FindAll("[data-testid='occurrence-row']").Count);
        Assert.Contains("AABBCC", cut.Markup);

        cut.Find("[data-testid='group-notes']").Change("revisar el endpoint");
        cut.Find("[data-testid='status-investigating']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(client.StatusSet));
        Assert.Equal("investigating", client.StatusSet!.Status);
        Assert.Equal("revisar el endpoint", client.StatusSet.Notes);
        cut.WaitForAssertion(() => Assert.Contains("Investigando", cut.Find("[data-testid='current-status']").TextContent));
    }

    [Fact]
    public void Event_group_detail_offers_to_retry_all_pending_events()
    {
        var client = Setup();
        client.Groups.Add(Group("event_failure", "GSM.Evt.A|GSM.Hnd.B"));

        Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/logs/group?kind=event_failure&fingerprint=" + Uri.EscapeDataString("GSM.Evt.A|GSM.Hnd.B"));
        var cut = RenderComponent<GroupDetail>();
        cut.Find("[data-testid='retry-group']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(client.GroupRetried));
        Assert.Equal("GSM.Evt.A", client.GroupRetried!.EventType);
        Assert.Equal("GSM.Hnd.B", client.GroupRetried.HandlerType);
    }

    [Fact]
    public void Trace_search_groups_the_records_by_request_and_shows_several_matches()
    {
        var client = Setup();
        var now = DateTimeOffset.UtcNow;
        client.TraceRecords.Add(new TraceRecordItem
        {
            Source = "errors", Id = Guid.NewGuid(), OccurredAt = now, TraceId = "7f3a2c" + new string('1', 26), TraceCode = "7F3A2C",
            Summary = "Boom: falló",
        });
        client.TraceRecords.Add(new TraceRecordItem
        {
            Source = "business_rejections", Id = Guid.NewGuid(), OccurredAt = now, TraceId = "7f3a2c" + new string('1', 26), TraceCode = "7F3A2C",
            Summary = "Cupo lleno", StatusCode = 409,
        });
        client.TraceRecords.Add(new TraceRecordItem
        {
            Source = "errors", Id = Guid.NewGuid(), OccurredAt = now.AddDays(-1), TraceId = "7f3a2c" + new string('2', 26), TraceCode = "7F3A2C",
            Summary = "Otra petición",
        });
        var cut = RenderComponent<TraceSearchPanel>();

        cut.Find("[data-testid='trace-code']").Input("7f3a2c");
        cut.Find("[data-testid='trace-search']").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='trace-request']").Count));
        Assert.Equal(3, cut.FindAll("[data-testid='trace-record']").Count);
        Assert.Equal("7f3a2c", client.SearchedCode);
        Assert.Contains("Cupo lleno", cut.Markup);
    }

    [Fact]
    public void Retrying_an_event_calls_the_api_and_refreshes_the_list()
    {
        var client = Setup();
        var pending = Failure();
        client.Events.Add(pending);
        var cut = RenderComponent<EventFailuresPanel>();

        Assert.Single(cut.FindAll("[data-testid='event-row']"));
        cut.Find("[data-testid='retry-one']").Click();

        cut.WaitForAssertion(() => Assert.Contains(pending.Id, client.Retried));
        // Resuelto: sale del filtro "pendientes".
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='event-row']")));
        Assert.NotNull(cut.Find("[data-testid='events-empty']"));
    }

    [Fact]
    public void A_not_retryable_event_stays_listed_with_the_reason()
    {
        var client = Setup();
        client.RetryOutcome = "not_retryable";
        client.Events.Add(Failure());
        var cut = RenderComponent<EventFailuresPanel>();

        cut.Find("[data-testid='retry-one']").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("datos que el log no guarda", cut.Find("[data-testid='retry-note']").TextContent));
        Assert.Single(cut.FindAll("[data-testid='event-row']"));
    }

    [Fact]
    public void Alerts_panel_lists_each_alert_with_the_delivery_state_of_both_channels()
    {
        var client = Setup();
        client.Alerts.Add(new PlatformAlertItem
        {
            Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Kind = "new_error",
            Title = "Falla nueva: System.InvalidOperationException", Body = "Dónde: /api/members/{id}",
            Link = "/admin/logs", EmailStatus = "sent", PushStatus = "skipped",
        });
        client.Alerts.Add(new PlatformAlertItem
        {
            Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-40), Kind = "process_stopped",
            Title = "Proceso detenido: notifications_dispatcher", Body = "Su último latido fue hace 9 minutos.",
            EmailStatus = "failed", PushStatus = "pending",
        });

        var cut = RenderComponent<AlertsPanel>();

        var rows = cut.FindAll("[data-testid='alert-row']");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Falla nueva", rows[0].TextContent);
        Assert.Contains("Enviado", rows[0].QuerySelector("[data-testid='alert-email']")!.TextContent);
        Assert.Contains("No aplica", rows[0].QuerySelector("[data-testid='alert-push']")!.TextContent);
        Assert.Contains("Proceso detenido", rows[1].TextContent);
        Assert.Contains("Falló", rows[1].QuerySelector("[data-testid='alert-email']")!.TextContent);
        Assert.Contains("Pendiente", rows[1].QuerySelector("[data-testid='alert-push']")!.TextContent);
    }

    [Fact]
    public void Alerts_panel_says_so_when_nothing_was_ever_raised()
    {
        Setup();

        var cut = RenderComponent<AlertsPanel>();

        Assert.NotNull(cut.Find("[data-testid='alerts-empty']"));
    }
}
