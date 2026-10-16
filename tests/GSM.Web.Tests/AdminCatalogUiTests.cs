using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Web.Pages.Admin.Catalog;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace GSM.Web.Tests;

/// <summary>S4: pantallas de catálogos del sistema en <c>/admin/catalog</c>.</summary>
public class AdminCatalogUiTests : TestContext
{
    private static readonly Guid ModelId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid ExerciseId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    private sealed class FakeApi : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path));

            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(new
                {
                    id = ModelId, name = "Prensa 45", type = "Machine", trackedPerUnit = true, isActive = path.Contains("/activate"),
                }));
            }

            string json;
            if (path.StartsWith("/api/admin/catalog/equipment-models"))
            {
                json = JsonSerializer.Serialize(new object[]
                {
                    new { id = ModelId, name = "Prensa 45", type = "Machine", imageUrl = (string?)null, trackedPerUnit = true, isActive = true },
                    new { id = Guid.NewGuid(), name = "Polea vieja", type = "Cable", imageUrl = "https://img.test/p.png", trackedPerUnit = true, isActive = false },
                });
            }
            else if (path.StartsWith("/api/admin/catalog/exercises"))
            {
                json = JsonSerializer.Serialize(new object[]
                {
                    new
                    {
                        id = ExerciseId, isSystem = true, name = "Press de banca", primaryMuscle = "Chest", muscleGroup = "UpperBody",
                        secondaryMuscles = Array.Empty<string>(), equipment = new[] { "Barbell" }, exerciseType = "Strength",
                        trackingType = "RepsWeight", difficulty = "Intermediate", isActive = true,
                    },
                });
            }
            else
            {
                json = "[]";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }

        private static HttpResponseMessage Json(object body)
            => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }

    private readonly FakeApi _api = new();

    public AdminCatalogUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IAdminCatalogClient>(new AdminCatalogClient(
            new HttpClient(_api) { BaseAddress = new Uri("http://localhost") }));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("root");
        auth.SetRoles("superadmin");
    }

    [Fact]
    public void Labels_map_equipment_types_both_ways_and_keep_only_recommended_alternatives()
    {
        Assert.Equal(0, CatalogLabels.EquipmentTypeValue("Barbell"));
        Assert.Equal(3, CatalogLabels.EquipmentTypeValue("Machine"));
        Assert.Equal(5, CatalogLabels.EquipmentTypeValue("Smith_machine"));
        Assert.Equal(15, CatalogLabels.EquipmentTypeValue("Cardio_machine"));
        Assert.Equal(0, CatalogLabels.EquipmentTypeValue("Desconocido"));

        var alternatives = new[]
        {
            new ExerciseAlternativeResponse { Name = "Zeta", Source = "recommended" },
            new ExerciseAlternativeResponse { Name = "Calculada", Source = "suggested" },
            new ExerciseAlternativeResponse { Name = "Alfa", Source = "recommended" },
        };
        Assert.Equal(["Alfa", "Zeta"], CatalogLabels.RecommendedOnly(alternatives).Select(a => a.Name));
        Assert.Empty(CatalogLabels.RecommendedOnly(null));

        Assert.Equal("Activo", CatalogLabels.StatusLabel(true));
        Assert.Equal("Inactivo", CatalogLabels.StatusLabel(false));
        Assert.Equal("Una fila por unidad", CatalogLabels.UnitModeLabel(true));
        Assert.Equal("Con cantidad", CatalogLabels.UnitModeLabel(false));
    }

    [Fact]
    public void Models_tab_lists_the_catalog_and_offers_deactivate_or_reactivate_by_state()
    {
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<ModelsTab>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=models-table]")));
        Assert.Contains("Prensa 45", cut.Markup);
        Assert.Contains("Polea vieja", cut.Markup);
        Assert.Contains("Sin imagen", cut.Markup);
        Assert.Contains(_api.Requests, r => r.Path.StartsWith("/api/admin/catalog/equipment-models"));

        // El activo se desactiva; el inactivo se reactiva.
        Assert.NotNull(cut.Find($"[data-testid=deactivate-model-{ModelId}]"));
        Assert.Single(cut.FindAll("[data-testid^=activate-model-]"));
    }

    [Fact]
    public void Deactivating_a_model_posts_to_the_deactivate_endpoint()
    {
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<ModelsTab>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"[data-testid=deactivate-model-{ModelId}]")));

        cut.Find($"[data-testid=deactivate-model-{ModelId}]").Click();

        cut.WaitForAssertion(() => Assert.Contains(_api.Requests,
            r => r.Method == HttpMethod.Post && r.Path == $"/api/admin/catalog/equipment-models/{ModelId}/deactivate"));
    }

    [Fact]
    public void Exercises_tab_lists_system_exercises_with_their_summary_and_actions()
    {
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<ExercisesTab>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=exercises-table]")));
        Assert.Contains("Press de banca", cut.Markup);
        Assert.NotNull(cut.Find($"[data-testid=alternatives-{ExerciseId}]"));
        Assert.NotNull(cut.Find($"[data-testid=models-{ExerciseId}]"));
        Assert.NotNull(cut.Find($"[data-testid=deactivate-{ExerciseId}]"));
        Assert.Contains(_api.Requests, r => r.Path.StartsWith("/api/admin/catalog/exercises"));
    }
}
