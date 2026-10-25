using System.Net;
using System.Text;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Exercises;
using GSM.UI.Shared.Formatting;
using GSM.UI.Shared.Routines;
using Microsoft.Extensions.DependencyInjection;
using ExercisesPage = GSM.Panel.UI.Pages.Exercises.Index;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>R3: catálogo de ejercicios del panel (campos nuevos, filtro en dos pasos) y grupos del día.</summary>
public class ExerciseCatalogUiTests : TestContext
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
        }
    }

    private readonly RecordingHandler _handler = new();

    public ExerciseCatalogUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IExercisesClient>(
            new ExercisesClient(new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }));
    }

    private async Task<IRenderedComponent<MudDialogProvider>> OpenFormAsync()
    {
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<ExerciseFormDialog>("Nuevo ejercicio"));
        return provider;
    }

    private static string? SelectedValue(IRenderedComponent<MudDialogProvider> provider, string testId)
        => provider.Find($"select[data-testid='{testId}']").QuerySelectorAll("option")
            .FirstOrDefault(o => o.HasAttribute("selected"))?.GetAttribute("value");

    [Fact]
    public async Task Exercise_form_has_the_new_fields_with_spanish_labels()
    {
        var provider = await OpenFormAsync();

        Assert.Equal("0", SelectedValue(provider, "exercise-type"));   // fuerza
        Assert.Equal("0", SelectedValue(provider, "tracking-type"));   // repeticiones y peso
        Assert.Equal("1", SelectedValue(provider, "difficulty"));      // intermedio
        Assert.Single(provider.FindAll("[data-testid='unilateral']"));

        var types = provider.Find("select[data-testid='exercise-type']").QuerySelectorAll("option").Select(o => o.TextContent);
        Assert.Equal(["Fuerza", "Cardio", "Movilidad", "Estiramiento", "Pliometría"], types);
        Assert.Contains("Nivel", provider.Markup);
        Assert.Contains("Zona:", provider.Markup); // la zona sale del músculo principal
    }

    [Fact]
    public async Task Picking_a_type_suggests_what_is_measured_until_the_user_picks_it_by_hand()
    {
        var provider = await OpenFormAsync();

        provider.Find("select[data-testid='exercise-type']").Change("2");   // movilidad
        Assert.Equal("2", SelectedValue(provider, "tracking-type"));        // tiempo

        provider.Find("select[data-testid='exercise-type']").Change("1");   // cardio
        Assert.Equal("4", SelectedValue(provider, "tracking-type"));        // tiempo y distancia

        provider.Find("select[data-testid='tracking-type']").Change("1");   // la persona elige "repeticiones"
        provider.Find("select[data-testid='exercise-type']").Change("2");   // y cambia el tipo otra vez
        Assert.Equal("1", SelectedValue(provider, "tracking-type"));        // no se le pisa lo elegido
    }

    [Fact]
    public void Muscle_filter_is_two_steps_group_first_and_then_only_its_muscles()
    {
        var cut = RenderComponent<ExercisesPage>();

        var muscle = cut.Find("select[data-testid='filter-muscle']");
        Assert.True(muscle.HasAttribute("disabled"));          // sin zona no hay segundo paso
        Assert.Single(muscle.QuerySelectorAll("option"));      // solo "Todos"

        // En el celular los filtros se pliegan tras "Filtros"; abrirlos no los quita del DOM.
        Assert.Equal("Filtros", cut.Find("[data-testid='filters-toggle']").TextContent.Trim());
        Assert.DoesNotContain("is-open", cut.Find("#exercise-filters").ClassName);
        cut.Find("[data-testid='filters-toggle']").Click();
        Assert.Contains("is-open", cut.Find("#exercise-filters").ClassName);

        cut.Find("select[data-testid='filter-group']").Change("1"); // tren inferior
        Assert.Equal("Filtros (1)", cut.Find("[data-testid='filters-toggle']").TextContent.Trim());

        var options = cut.Find("select[data-testid='filter-muscle']").QuerySelectorAll("option").Select(o => o.TextContent).ToList();
        Assert.False(cut.Find("select[data-testid='filter-muscle']").HasAttribute("disabled"));
        Assert.Contains("Isquiotibiales", options);
        Assert.Contains("Aductores", options);
        Assert.DoesNotContain("Pecho", options);
        Assert.DoesNotContain("Lumbar", options);

        Assert.Contains(_handler.Requests, r => r.Contains("muscleGroup=1"));

        cut.Find("select[data-testid='filter-muscle']").Change("7"); // isquiotibiales
        Assert.Contains(_handler.Requests, r => r.Contains("primaryMuscle=7") && r.Contains("muscleGroup=1"));

        // Cambiar de zona limpia el músculo, que podría no ser de la zona nueva.
        cut.Find("select[data-testid='filter-group']").Change("2"); // core
        var last = _handler.Requests[^1];
        Assert.Contains("muscleGroup=2", last);
        Assert.DoesNotContain("primaryMuscle", last);
    }

    [Fact]
    public void Catalog_filters_send_type_difficulty_and_show_hidden()
    {
        var cut = RenderComponent<ExercisesPage>();

        cut.Find("select[data-testid='filter-type']").Change("1");
        Assert.Contains("exerciseType=1", _handler.Requests[^1]);

        cut.Find("select[data-testid='filter-difficulty']").Change("2");
        Assert.Contains("difficulty=2", _handler.Requests[^1]);
        Assert.Contains("exerciseType=1", _handler.Requests[^1]);
        Assert.DoesNotContain("includeHidden=true", _handler.Requests[^1]);

        cut.Find("input[data-testid='filter-hidden']").Change(true);
        Assert.Contains("includeHidden=true", _handler.Requests[^1]);
    }

    [Fact]
    public void Labels_and_group_mapping_cover_every_value()
    {
        Assert.Equal(18, RoutineLabels.Muscles.Length);
        Assert.DoesNotContain(RoutineLabels.Muscles, m => m.Label == "Cardio");

        // Todo músculo cae en exactamente una zona.
        var grouped = RoutineLabels.MuscleGroups.SelectMany(g => RoutineLabels.MusclesOfGroup(g.Value)).Select(m => m.Value).ToList();
        Assert.Equal(RoutineLabels.Muscles.Select(m => m.Value).Order(), grouped.Order());

        Assert.Equal("Tren inferior", RoutineLabels.MuscleGroupLabel("LowerBody"));
        Assert.Equal("Trapecio", RoutineLabels.MuscleLabel("Traps"));
        Assert.Equal("Tiempo y distancia", RoutineLabels.TrackingTypeLabel("TimeDistance"));
        Assert.Equal(4, RoutineLabels.SuggestedTracking(1));
        Assert.Equal(2, RoutineLabels.SuggestedTracking(3));
        Assert.Equal(0, RoutineLabels.SuggestedTracking(0));
    }

    [Fact]
    public void A_day_shows_the_groups_it_works_and_uses_them_as_the_suggested_name()
    {
        var draft = RoutineDraft.Empty();
        draft.Name = "Mixta";
        draft.Days[0].Exercises =
        [
            new DraftExercise { Exercise = new ExerciseResponse { Id = Guid.NewGuid(), Name = "Press", MuscleGroup = "UpperBody" } },
            new DraftExercise { Exercise = new ExerciseResponse { Id = Guid.NewGuid(), Name = "Plancha", MuscleGroup = "Core" } },
            new DraftExercise { Exercise = new ExerciseResponse { Id = Guid.NewGuid(), Name = "Remo", MuscleGroup = "UpperBody" } },
        ];

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<RoutineForm>(p => p.Add(x => x.Draft, draft));

        Assert.Equal("Tren superior · Core", draft.Days[0].SuggestedName);
        Assert.Contains("Tren superior · Core", cut.Find("[data-testid='day-groups-0']").TextContent);

        // Un día sin ejercicios no sugiere nada y la solicitud no inventa un nombre.
        var empty = new DraftDay();
        Assert.Null(empty.SuggestedName);
        Assert.Null(draft.ToRequest().Days.First().Name);
    }
}
