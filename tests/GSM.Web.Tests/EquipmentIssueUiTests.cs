using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.Panel.UI.Layout;
using GSM.Panel.UI.Pages.Equipment;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// E4: reportes de falla. Panel (lista, detalle con sus acciones, reportar desde la ficha de la máquina, tarjetas del
/// dashboard, aviso en pantalla de los urgentes) y app del miembro (botón «Reportar falla»).
/// Los nombres del cliente generado (<c>IEquipmentClient.ListEquipmentIssuesAsync/GetEquipmentIssueAsync/
/// UpdateEquipmentIssueAsync/ReportEquipmentIssueByStaffAsync/GetEquipmentSummaryAsync</c> e
/// <c>IMemberEquipmentClient.ListMyEquipmentIssuesAsync/ReportMyEquipmentIssueAsync</c>) son los que NSwag debería generar.
/// </summary>
public class EquipmentIssueUiTests : TestContext
{
    private static readonly Guid MachineId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid IssueId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid OtherIssueId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private static object Issue(
        Guid id, string reason = "DamagedPart", string status = "Open", bool urgent = false, bool seen = false)
        => new
        {
            id, itemId = MachineId, itemName = "Prensa 45", itemZone = "Pierna", reason, description = "Se soltó un tornillo",
            status, isUrgent = urgent, isSeen = seen, reportedByRole = "Member", reportedByName = "Ana Torres",
            createdAt = "2026-10-14T23:40:00+00:00",
        };

    private sealed class FakeApi : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

        public List<object> Issues { get; set; } = [];

        public object Detail { get; set; } = Issue(IssueId);

        public string MachineStatus { get; set; } = "Available";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path, body));

            var status = HttpStatusCode.OK;
            string json;
            if (request.Method == HttpMethod.Put && path.StartsWith("/api/equipment/issues/"))
            {
                json = JsonSerializer.Serialize(Detail);
            }
            else if (request.Method == HttpMethod.Post && path.EndsWith("/issues"))
            {
                status = HttpStatusCode.Created;
                json = JsonSerializer.Serialize(Issue(Guid.NewGuid()));
            }
            else if (path.StartsWith("/api/equipment/issues/"))
            {
                json = JsonSerializer.Serialize(Detail);
            }
            else if (path.StartsWith("/api/equipment/issues"))
            {
                json = JsonSerializer.Serialize(Issues);
            }
            else if (path.StartsWith("/api/equipment/summary"))
            {
                json = JsonSerializer.Serialize(new { outOfService = 2, inMaintenance = 1, openIssues = 3, urgentIssues = 1 });
            }
            else if (path.EndsWith("/history"))
            {
                json = "[]";
            }
            else
            {
                json = JsonSerializer.Serialize(new
                {
                    id = MachineId, name = "Prensa 45", type = "Machine", hasOwnPhoto = true, quantity = 1, status = MachineStatus,
                    isActive = true, zone = "Pierna", isOverdue = false,
                });
            }

            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private readonly FakeApi _api = new();

    private void UsePanel()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IEquipmentClient>(
            new EquipmentClient(new HttpClient(_api) { BaseAddress = new Uri("http://localhost") }));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("luis");
        auth.SetRoles("staff");
    }

    private static JsonDocument Body((HttpMethod Method, string Path, string Body) request) => JsonDocument.Parse(request.Body);

    // --- labels ---

    [Fact]
    public void Labels_cover_every_reason_and_status_in_spanish()
    {
        Assert.Equal("Pieza dañada", EquipmentIssueLabels.Reason("DamagedPart"));
        Assert.Equal("Insegura", EquipmentIssueLabels.Reason("Unsafe"));
        Assert.Equal("En revisión", EquipmentIssueLabels.Status("InProgress"));
        Assert.True(EquipmentIssueLabels.IsUnclosed("Open"));
        Assert.True(EquipmentIssueLabels.IsUnclosed("InProgress"));
        Assert.False(EquipmentIssueLabels.IsUnclosed("Resolved"));
        Assert.Equal(7, EquipmentIssueLabels.Reasons.Length);
    }

    // --- lista del panel ---

    [Fact]
    public void The_issues_page_lists_open_reports_with_urgent_and_unseen_marks()
    {
        UsePanel();
        _api.Issues =
        [
            Issue(IssueId, "Unsafe", urgent: true, seen: false),
            Issue(OtherIssueId, "Dirty", seen: true),
        ];

        var cut = RenderComponent<Issues>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='open-issue']").Count));
        Assert.Single(cut.FindAll("[data-testid='urgent-chip']"));
        Assert.Single(cut.FindAll("[data-testid='unseen-chip']"));
        Assert.Contains("Insegura", cut.Markup);
        Assert.Contains("Ana Torres", cut.Markup);
        Assert.Contains(_api.Requests, r => r.Path.Contains("openOnly=true"));
    }

    [Fact]
    public void The_issues_page_says_when_nothing_is_open()
    {
        UsePanel();

        var cut = RenderComponent<Issues>();

        cut.WaitForAssertion(() => Assert.Contains("No hay reportes abiertos", cut.Find("[data-testid='issues-empty']").TextContent));
    }

    // --- detalle: En revisión, mantenimiento, resolver, descartar ---

    [Fact]
    public void An_unsafe_report_shows_the_urgent_alert()
    {
        UsePanel();
        _api.Detail = Issue(IssueId, "Unsafe", urgent: true, seen: true);

        var cut = RenderComponent<IssueDetail>(p => p.Add(x => x.Id, IssueId));

        cut.WaitForAssertion(() => Assert.Contains("insegura", cut.Find("[data-testid='urgent-alert']").TextContent));
    }

    [Fact]
    public void Putting_a_report_in_review_sends_the_status()
    {
        UsePanel();
        _api.Detail = Issue(IssueId, seen: true);
        var cut = RenderComponent<IssueDetail>(p => p.Add(x => x.Id, IssueId));
        cut.WaitForElement("[data-testid='issue-review']");

        cut.Find("[data-testid='issue-review']").Click();

        cut.WaitForAssertion(() =>
        {
            var put = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Put);
            Assert.EndsWith($"/api/equipment/issues/{IssueId}", put.Path);
            using var doc = Body(put);
            Assert.Equal(1, doc.RootElement.GetProperty("status").GetInt32());
        });
    }

    [Fact]
    public void Resolving_needs_a_resolution_and_sends_it()
    {
        UsePanel();
        var provider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<IssueDetail>(p => p.Add(x => x.Id, IssueId));
        cut.WaitForElement("[data-testid='issue-resolve']");

        cut.Find("[data-testid='issue-resolve']").Click();
        provider.WaitForElement("[data-testid='resolution-save']");

        // Sin explicación no se llama a la API.
        provider.Find("[data-testid='resolution-save']").Click();
        Assert.Contains("cómo se resolvió", provider.Find("[data-testid='resolution-error']").TextContent);
        Assert.DoesNotContain(_api.Requests, r => r.Method == HttpMethod.Put);

        provider.Find("textarea").Input("Cambié el cable");
        provider.Find("[data-testid='resolution-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var put = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Put);
            using var doc = Body(put);
            Assert.Equal(2, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Cambié el cable", doc.RootElement.GetProperty("resolution").GetString());
        });
    }

    [Fact]
    public void Dismissing_sends_the_dismissed_status_with_its_reason()
    {
        UsePanel();
        var provider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<IssueDetail>(p => p.Add(x => x.Id, IssueId));
        cut.WaitForElement("[data-testid='issue-dismiss']");

        cut.Find("[data-testid='issue-dismiss']").Click();
        provider.WaitForElement("[data-testid='resolution-save']");
        provider.Find("textarea").Input("Estaba limpia");
        provider.Find("[data-testid='resolution-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var put = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Put);
            using var doc = Body(put);
            Assert.Equal(3, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Estaba limpia", doc.RootElement.GetProperty("resolution").GetString());
        });
    }

    [Fact]
    public void The_report_offers_to_put_the_machine_in_maintenance_only_while_it_is_available()
    {
        UsePanel();
        var cut = RenderComponent<IssueDetail>(p => p.Add(x => x.Id, IssueId));
        cut.WaitForElement("[data-testid='issue-maintenance']");
        Assert.Contains("disponible", cut.Find("[data-testid='item-state']").TextContent);
    }

    [Fact]
    public void A_machine_already_in_maintenance_is_not_offered_again()
    {
        UsePanel();
        _api.MachineStatus = "Maintenance";

        var cut = RenderComponent<IssueDetail>(p => p.Add(x => x.Id, IssueId));

        cut.WaitForAssertion(() => Assert.Contains("en mantenimiento", cut.Find("[data-testid='item-state']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='issue-maintenance']"));
    }

    [Fact]
    public void A_closed_report_shows_its_resolution_and_no_actions()
    {
        UsePanel();
        _api.Detail = new
        {
            id = IssueId, itemId = MachineId, itemName = "Prensa 45", reason = "NotWorking", status = "Resolved", isUrgent = false,
            isSeen = true, reportedByRole = "Member", reportedByName = "Ana Torres", createdAt = "2026-10-14T23:40:00+00:00",
            resolvedAt = "2026-10-15T14:00:00+00:00", resolvedByName = "Luis Pérez", resolution = "Cambié el cable",
        };

        var cut = RenderComponent<IssueDetail>(p => p.Add(x => x.Id, IssueId));

        cut.WaitForAssertion(() => Assert.Contains("Cambié el cable", cut.Find("[data-testid='issue-resolution']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='issue-resolve']"));
        Assert.Empty(cut.FindAll("[data-testid='issue-review']"));
    }

    // --- ficha de la máquina: reportes abiertos y reportar ---

    [Fact]
    public void The_machine_page_lists_its_open_reports()
    {
        UsePanel();
        _api.Issues = [Issue(IssueId, "NotWorking"), Issue(OtherIssueId, "Unsafe", urgent: true)];

        var cut = RenderComponent<Detail>(p => p.Add(x => x.Id, MachineId));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.Find("[data-testid='open-issues']").QuerySelectorAll("li").Length));
        Assert.Contains("No funciona", cut.Markup);
        Assert.Contains("URGENTE", cut.Markup);
        Assert.Contains("revísalos", cut.Find("[data-testid='open-issues-hint']").TextContent);
    }

    [Fact]
    public void The_staff_reports_a_failure_from_the_machine_page()
    {
        UsePanel();
        var provider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<Detail>(p => p.Add(x => x.Id, MachineId));
        cut.WaitForElement("[data-testid='report-issue']");

        cut.Find("[data-testid='report-issue']").Click();
        provider.WaitForElement("[data-testid='report-save']");

        // «Otro» sin descripción no sale.
        provider.Find("select[data-testid='issue-reason']").Change("6");
        provider.Find("[data-testid='report-save']").Click();
        Assert.Contains("necesita una descripción", provider.Find("[data-testid='report-error']").TextContent);
        Assert.DoesNotContain(_api.Requests, r => r.Method == HttpMethod.Post);

        provider.Find("select[data-testid='issue-reason']").Change("2");
        provider.Find("[data-testid='report-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var post = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Post);
            Assert.EndsWith($"/api/equipment/{MachineId}/issues", post.Path);
            using var doc = Body(post);
            Assert.Equal(2, doc.RootElement.GetProperty("reason").GetInt32());
        });
    }

    // --- tarjetas del dashboard ---

    [Fact]
    public void The_dashboard_cards_show_machines_out_of_service_and_open_reports()
    {
        UsePanel();

        var cut = RenderComponent<EquipmentCards>();

        cut.WaitForAssertion(() => Assert.Contains("2", cut.Find("[data-testid='equipment-out']").TextContent));
        Assert.Contains("1 en mantenimiento", cut.Find("[data-testid='equipment-out']").TextContent);
        var issues = cut.Find("[data-testid='equipment-issues']").TextContent;
        Assert.Contains("3", issues);
        Assert.Contains("1 urgente", issues);
    }

    // --- campana: aviso en pantalla de los urgentes ---

    [Fact]
    public void An_open_urgent_notice_raises_an_on_screen_alert_once()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var bell = DispatchProxy.Create<IStaffNotificationsClient, FakeClient>();
        var fake = (FakeClient)(object)bell;
        fake.Handlers["OpenCountAsync"] = _ => new StaffNotificationCount { Count = 2 };
        fake.Handlers["Notifications2Async"] = _ => new PagedResultOfStaffNotificationItem
        {
            Page = 1, PageSize = 8, Total = 2,
            Items =
            [
                new StaffNotificationItem
                {
                    Id = Guid.NewGuid(), Type = "EquipmentIssueReported", Title = "URGENTE · Prensa 45 es insegura",
                    Body = "Reportado por Ana Torres", Priority = "Urgent", RequiresAction = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                new StaffNotificationItem
                {
                    Id = Guid.NewGuid(), Type = "EquipmentIssueReported", Title = "Remo — sucia", Body = "Reportado por Luis",
                    Priority = "Normal", RequiresAction = true, CreatedAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        Services.AddSingleton(bell);
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("Ana");
        auth.SetPolicies(GsmPolicies.RequireStaff);
        RenderComponent<MudSnackbarProvider>();
        RenderComponent<MudPopoverProvider>();

        RenderComponent<StaffBell>();

        var snackbar = Services.GetRequiredService<ISnackbar>();
        Assert.Single(snackbar.ShownSnackbars);
        Assert.Contains("insegura", snackbar.ShownSnackbars.Single().Message.ToString());
    }

    // --- app del miembro ---

    private static EquipmentMachineInfo Machine(Guid id, string name, string status = "Available")
        => new() { Id = id, Name = name, Type = "Machine", Status = status };

    private (FakeClient Fake, IRenderedComponent<EquipmentReport> Cut, IRenderedComponent<MudDialogProvider> Provider) RenderMember(
        bool flag, IReadOnlyList<EquipmentMachineInfo> machines, List<MyEquipmentIssueResponse>? mine = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var api = DispatchProxy.Create<IMemberEquipmentClient, FakeClient>();
        var fake = (FakeClient)(object)api;
        fake.Handlers["ListMyEquipmentIssuesAsync"] = _ => mine ?? [];
        fake.Handlers["ReportMyEquipmentIssueAsync"] = args => new MyEquipmentIssueResponse
        {
            Id = Guid.NewGuid(), ItemId = (Guid)args[0]!, ItemName = "Prensa 45", Reason = "NotWorking", Status = "Open",
        };
        Services.AddSingleton(api);

        var gym = new GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { MembersCanReportEquipment = flag } }, false);
        Services.AddSingleton(gym);

        var provider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<EquipmentReport>(p => p.Add(x => x.Machines, machines));
        return (fake, cut, provider);
    }

    [Fact]
    public void With_the_flag_off_the_member_sees_no_report_button_and_nothing_is_requested()
    {
        var (fake, cut, _) = RenderMember(false, [Machine(MachineId, "Prensa 45")]);

        Assert.Empty(cut.FindAll("[data-testid='report-issue']"));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void The_member_reports_in_two_taps_and_is_thanked()
    {
        var (fake, cut, provider) = RenderMember(true, [Machine(MachineId, "Prensa 45")]);
        cut.WaitForElement("[data-testid='report-issue']");

        cut.Find("[data-testid='report-issue']").Click();
        provider.WaitForElement("[data-testid='report-save']");
        provider.Find("select[data-testid='issue-reason']").Change("5");   // insegura
        provider.Find("[data-testid='report-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(fake.Calls, c => c.Method == "ReportMyEquipmentIssueAsync");
            Assert.Equal(MachineId, call.Args[0]);
            var request = (ReportEquipmentIssueRequest)call.Args[1]!;
            Assert.Equal(EquipmentIssueLabels.UnsafeValue, request.Reason);
        });

        // Tras enviar, el botón pasa a "ya reportaste" cuando el servidor lo confirma al recargar.
        Assert.Contains(fake.Calls, c => c.Method == "ListMyEquipmentIssuesAsync");
    }

    [Fact]
    public void The_member_cannot_send_other_without_a_line()
    {
        var (fake, cut, provider) = RenderMember(true, [Machine(MachineId, "Prensa 45")]);
        cut.WaitForElement("[data-testid='report-issue']");

        cut.Find("[data-testid='report-issue']").Click();
        provider.WaitForElement("[data-testid='report-save']");
        provider.Find("select[data-testid='issue-reason']").Change("6");
        provider.Find("[data-testid='report-save']").Click();

        Assert.Contains("necesita una línea", provider.Find("[data-testid='report-error']").TextContent);
        Assert.DoesNotContain(fake.Calls, c => c.Method == "ReportMyEquipmentIssueAsync");
    }

    [Fact]
    public void An_already_reported_machine_shows_the_status_instead_of_the_button()
    {
        var (_, cut, _) = RenderMember(
            true, [Machine(MachineId, "Prensa 45")],
            [new MyEquipmentIssueResponse { Id = IssueId, ItemId = MachineId, ItemName = "Prensa 45", Reason = "Dirty", Status = "InProgress" }]);

        cut.WaitForAssertion(() =>
            Assert.Contains("Ya reportaste esta máquina · En revisión", cut.Find("[data-testid='already-reported']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='report-issue']"));
    }

    [Fact]
    public void A_closed_report_does_not_block_a_new_one()
    {
        var (_, cut, _) = RenderMember(
            true, [Machine(MachineId, "Prensa 45")],
            [new MyEquipmentIssueResponse { Id = IssueId, ItemId = MachineId, ItemName = "Prensa 45", Reason = "Dirty", Status = "Resolved" }]);

        cut.WaitForElement("[data-testid='report-issue']");
        Assert.Empty(cut.FindAll("[data-testid='already-reported']"));
    }

    [Fact]
    public void With_several_possible_machines_the_member_chooses_which_one()
    {
        var second = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
        var (fake, cut, provider) = RenderMember(
            true, [Machine(MachineId, "Prensa 45"), Machine(second, "Prensa horizontal")]);
        cut.WaitForElement("[data-testid='report-issue']");

        cut.Find("[data-testid='report-issue']").Click();
        provider.WaitForElement("select[data-testid='issue-machine']");
        provider.Find("select[data-testid='issue-machine']").Change(second.ToString());
        provider.Find("[data-testid='report-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(fake.Calls, c => c.Method == "ReportMyEquipmentIssueAsync");
            Assert.Equal(second, call.Args[0]);
        });
    }
}
