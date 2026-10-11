using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Equipment;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using EquipmentPage = GSM.Panel.UI.Pages.Equipment.Index;

namespace GSM.Web.Tests;

/// <summary>E2: cambio de estado de una máquina y su historial en el panel.</summary>
public class EquipmentStatusUiTests : TestContext
{
    private static readonly Guid MachineId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private sealed class FakeApi : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

        public string Status { get; set; } = "Maintenance";

        public bool Overdue { get; set; }

        public HttpStatusCode ChangeResult { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path, body));

            var status = HttpStatusCode.OK;
            string json;

            if (request.Method == HttpMethod.Post && path.EndsWith("/status"))
            {
                status = ChangeResult;
                json = ChangeResult == HttpStatusCode.OK
                    ? JsonSerializer.Serialize(Item("Available"))
                    : JsonSerializer.Serialize(new { title = "Conflicto", detail = "El equipo ya está en ese estado.", status = (int)ChangeResult });
            }
            else if (path.EndsWith("/history"))
            {
                json = JsonSerializer.Serialize(new object[]
                {
                    new
                    {
                        id = Guid.NewGuid(), fromStatus = "Available", toStatus = Status, reason = "Se soltó el cable",
                        expectedBackOn = "2026-11-20", changedByUserId = Guid.NewGuid(), changedByName = "Luis Pérez",
                        changedAt = "2026-11-10T15:30:00+00:00",
                    },
                });
            }
            else if (path.StartsWith("/api/equipment/zones"))
            {
                json = "[]";
            }
            else if (path.StartsWith($"/api/equipment/{MachineId}"))
            {
                json = JsonSerializer.Serialize(Item(Status));
            }
            else
            {
                json = JsonSerializer.Serialize(new[] { Item(Status) });
            }

            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private object Item(string status) => new
        {
            id = MachineId, name = "Prensa 45", type = "Machine", hasOwnPhoto = true, quantity = 1, status,
            isActive = true, zone = "Pierna", expectedBackOn = status == "Available" ? null : "2026-11-20", isOverdue = Overdue,
        };
    }

    private readonly FakeApi _api = new();

    public EquipmentStatusUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IEquipmentClient>(
            new EquipmentClient(new HttpClient(_api) { BaseAddress = new Uri("http://localhost") }));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("luis");
        auth.SetRoles("staff");
    }

    [Fact]
    public void Detail_shows_the_status_the_expected_return_and_the_history_with_author_and_reason()
    {
        var cut = RenderComponent<Detail>(p => p.Add(x => x.Id, MachineId));

        Assert.Contains("Prensa 45", cut.Markup);
        Assert.Contains("EN MANTENIMIENTO", cut.Find("[data-testid='detail-status']").TextContent.ToUpperInvariant());
        Assert.Contains("Se espera de vuelta", cut.Find("[data-testid='expected-back']").TextContent);

        var history = cut.Find("[data-testid='history']").TextContent;
        Assert.Contains("Disponible", history);
        Assert.Contains("En mantenimiento", history);
        Assert.Contains("Se soltó el cable", history);
        Assert.Contains("Luis Pérez", history);
        Assert.Empty(cut.FindAll("[data-testid='detail-overdue']"));
    }

    [Fact]
    public void Detail_flags_an_overdue_machine()
    {
        _api.Overdue = true;
        var cut = RenderComponent<Detail>(p => p.Add(x => x.Id, MachineId));

        Assert.Single(cut.FindAll("[data-testid='detail-overdue']"));
        Assert.Contains("sigue sin volver", cut.Find("[data-testid='expected-back']").TextContent);
    }

    [Fact]
    public void Inventory_row_shows_overdue_chip()
    {
        _api.Overdue = true;
        var cut = RenderComponent<EquipmentPage>();

        Assert.Single(cut.FindAll("[data-testid='overdue-chip']"));
        Assert.Single(cut.FindAll("[data-testid='change-status']"));
    }

    private async Task<IRenderedComponent<MudDialogProvider>> OpenDialogAsync(string currentStatus)
    {
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<EquipmentStatusDialog>
        {
            { x => x.EquipmentId, MachineId },
            { x => x.Name, "Prensa 45" },
            { x => x.CurrentStatus, currentStatus },
        };
        await provider.InvokeAsync(() => dialogs.ShowAsync<EquipmentStatusDialog>("Cambiar estado", parameters));
        return provider;
    }

    [Fact]
    public async Task Dialog_requires_a_reason_to_take_a_machine_out_of_service_and_sends_the_change()
    {
        var provider = await OpenDialogAsync("Available");

        // El estado actual no se ofrece como destino.
        var options = provider.Find("select[data-testid='status-select']").QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Equal(["1", "2"], options);

        // Sin motivo no se llama a la API.
        await provider.InvokeAsync(() => provider.Find("[data-testid='status-save']").Click());
        Assert.Contains("motivo", provider.Find("[data-testid='status-error']").TextContent);
        Assert.DoesNotContain(_api.Requests, r => r.Method == HttpMethod.Post);

        provider.Find("select[data-testid='status-select']").Change("2");
        provider.Find("textarea").Input("Motor quemado");
        await provider.InvokeAsync(() => provider.Find("[data-testid='status-save']").Click());

        var post = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Post);
        Assert.EndsWith($"/api/equipment/{MachineId}/status", post.Path);
        using var doc = JsonDocument.Parse(post.Body);
        Assert.Equal(2, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Motor quemado", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Dialog_returning_to_available_needs_no_reason_and_hides_the_return_date()
    {
        var provider = await OpenDialogAsync("Maintenance");

        Assert.Empty(provider.FindAll("[data-testid='status-back-on']")) ;   // el primer destino ofrecido es "Disponible"

        await provider.InvokeAsync(() => provider.Find("[data-testid='status-save']").Click());

        var post = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Post);
        using var doc = JsonDocument.Parse(post.Body);
        Assert.Equal(0, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("reason").ValueKind);
    }

    [Fact]
    public async Task Dialog_shows_the_server_message_on_a_conflict()
    {
        _api.ChangeResult = HttpStatusCode.Conflict;
        var provider = await OpenDialogAsync("Maintenance");

        await provider.InvokeAsync(() => provider.Find("[data-testid='status-save']").Click());

        Assert.Contains("ya está en ese estado", provider.Find("[data-testid='status-error']").TextContent);
    }
}
