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

/// <summary>E1: inventario del panel y "Agregar del catálogo".</summary>
public class EquipmentUiTests : TestContext
{
    private static readonly Guid BikeId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid DumbbellId = Guid.Parse("00000000-0000-0000-0000-0000000000d1");

    private sealed class FakeApi : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path, body));

            string json;
            var status = HttpStatusCode.OK;
            if (path.StartsWith("/api/equipment/models"))
            {
                json = JsonSerializer.Serialize(new object[]
                {
                    new { id = BikeId, name = "Bicicleta estática", type = "Cardio_machine", description = "Cardio", trackedPerUnit = true },
                    new { id = DumbbellId, name = "Mancuernas", type = "Dumbbell", trackedPerUnit = false },
                });
            }
            else if (path.StartsWith("/api/equipment/zones"))
            {
                json = "[\"Cardio\",\"Peso libre\"]";
            }
            else if (request.Method == HttpMethod.Post && path.StartsWith("/api/equipment/from-models"))
            {
                json = "[]";
                status = HttpStatusCode.Created;
            }
            else if (path.StartsWith("/api/equipment"))
            {
                json = JsonSerializer.Serialize(new object[]
                {
                    Item("Bicicleta estática 1", "Cardio_machine", hasPhoto: false),
                    Item("Prensa", "Machine", hasPhoto: true),
                });
            }
            else
            {
                json = "[]";
            }

            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static object Item(string name, string type, bool hasPhoto) => new
        {
            id = Guid.NewGuid(), name, type, hasOwnPhoto = hasPhoto, quantity = 1, status = "Available", isActive = true,
            zone = "Cardio",
        };
    }

    private readonly FakeApi _api = new();

    public EquipmentUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IEquipmentClient>(
            new EquipmentClient(new HttpClient(_api) { BaseAddress = new Uri("http://localhost") }));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("owner");
    }

    [Fact]
    public void Inventory_lists_the_machines_marks_missing_own_photo_and_offers_deactivate_to_the_owner()
    {
        var cut = RenderComponent<EquipmentPage>();

        Assert.Contains("Bicicleta estática 1", cut.Markup);
        Assert.Contains("Prensa", cut.Markup);
        Assert.Contains("Disponible", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Single(cut.FindAll("[data-testid='no-photo']"));            // solo la bicicleta carece de foto propia
        Assert.Equal(2, cut.FindAll("[data-testid='deactivate']").Count);  // el Owner puede dar de baja
        Assert.Single(cut.FindAll("[data-testid='add-from-catalog']"));
    }

    [Fact]
    public void Staff_does_not_see_the_deactivate_button()
    {
        var staff = new TestContext();
        staff.JSInterop.Mode = JSRuntimeMode.Loose;
        staff.Services.AddMudServices();
        staff.Services.AddSingleton<IEquipmentClient>(
            new EquipmentClient(new HttpClient(new FakeApi()) { BaseAddress = new Uri("http://localhost") }));
        var auth = staff.AddTestAuthorization();
        auth.SetAuthorized("luis");
        auth.SetRoles("staff");

        var cut = staff.RenderComponent<EquipmentPage>();

        Assert.Contains("Prensa", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='deactivate']"));
    }

    [Fact]
    public void Inventory_filters_send_type_and_status()
    {
        var cut = RenderComponent<EquipmentPage>();

        cut.Find("select[data-testid='filter-type']").Change("15");      // máquina de cardio
        Assert.Contains(_api.Requests, r => r.Path.Contains("type=15"));

        cut.Find("select[data-testid='filter-status']").Change("1");     // en mantenimiento
        Assert.Contains(_api.Requests, r => r.Path.Contains("status=1") && r.Path.Contains("type=15"));
    }

    private async Task<IRenderedComponent<MudDialogProvider>> OpenCatalogAsync()
    {
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<AddFromCatalogDialog>("Agregar del catálogo"));
        return provider;
    }

    [Fact]
    public async Task Catalog_groups_models_by_type_and_saves_everything_at_once()
    {
        var provider = await OpenCatalogAsync();

        var titles = provider.FindAll("[data-testid='group-title']").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(["Mancuerna", "Máquina de cardio"], titles);          // orden del enum
        Assert.True(provider.Find("[data-testid='catalog-save']").HasAttribute("disabled"));   // nada marcado

        provider.Find($"[data-testid='toggle-{BikeId}']").Change(true);
        provider.Find($"[data-testid='qty-{BikeId}']").Change("3");
        provider.Find($"[data-testid='toggle-{DumbbellId}']").Change(true);
        provider.Find($"[data-testid='qty-{DumbbellId}']").Change("20");

        Assert.Contains("2 seleccionados", provider.Find("[data-testid='picked-count']").TextContent);

        await provider.InvokeAsync(() => provider.Find("[data-testid='catalog-save']").Click());

        var post = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Post && r.Path.StartsWith("/api/equipment/from-models"));
        using var doc = JsonDocument.Parse(post.Body);
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var bike = items.Single(i => i.GetProperty("modelId").GetGuid() == BikeId);
        Assert.Equal(3, bike.GetProperty("quantity").GetInt32());
        Assert.Equal(JsonValueKind.Null, bike.GetProperty("zone").ValueKind);

        var dumbbells = items.Single(i => i.GetProperty("modelId").GetGuid() == DumbbellId);
        Assert.Equal(20, dumbbells.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Catalog_type_filter_hides_other_groups()
    {
        var provider = await OpenCatalogAsync();

        provider.Find("select[data-testid='catalog-filter-type']").Change("1");   // mancuerna

        var titles = provider.FindAll("[data-testid='group-title']").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(["Mancuerna"], titles);
    }

    [Fact]
    public async Task Machines_are_capped_at_fifty_units_in_the_form()
    {
        var provider = await OpenCatalogAsync();
        provider.Find($"[data-testid='toggle-{BikeId}']").Change(true);

        Assert.Equal("50", provider.Find($"[data-testid='qty-{BikeId}']").GetAttribute("max"));
    }
}
