using Bunit;
using GSM.Client.Generated;
using GSM.UI.Shared.Routines;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>R2: el formulario compartido de rutinas en modo semanal.</summary>
public class RoutineFormWeeklyTests : TestContext
{
    public RoutineFormWeeklyTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        // El buscador de ejercicios no se usa aquí: el cliente nunca llega a llamarse.
        Services.AddSingleton<IExercisesClient>(new ExercisesClient(new HttpClient { BaseAddress = new Uri("http://localhost") }));
    }

    // Los días después del primero arrancan plegados: se abren todos para revisar sus controles.
    private IRenderedComponent<RoutineForm> Render(RoutineDraft draft)
    {
        var cut = RenderComponent<RoutineForm>(p => p.Add(x => x.Draft, draft));
        for (var i = 1; i < draft.Days.Count; i++)
        {
            cut.Find($"[data-testid='day-toggle-{i}']").Click();
        }

        return cut;
    }

    [Fact]
    public void Only_the_first_day_starts_open_and_a_collapsed_day_shows_its_summary()
    {
        var draft = WeeklyDraft();
        var cut = RenderComponent<RoutineForm>(p => p.Add(x => x.Draft, draft));

        Assert.Equal("true", cut.Find("[data-testid='day-toggle-0']").GetAttribute("aria-expanded"));
        Assert.Equal("false", cut.Find("[data-testid='day-toggle-1']").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("[data-testid='weekday-1']"));
        Assert.Contains("Sin ejercicios", cut.Find("[data-testid='day-1']").TextContent);

        cut.Find("[data-testid='day-toggle-1']").Click();
        Assert.Single(cut.FindAll("[data-testid='weekday-1']"));
    }

    private static RoutineDraft WeeklyDraft()
    {
        var draft = RoutineDraft.Empty();
        draft.Name = "Semanal";
        draft.SetMode(RoutineDraft.WeeklyMode);
        draft.AddDay();
        draft.Days[1].Name = "Pierna";
        return draft;
    }

    [Fact]
    public void Switching_to_weekly_assigns_a_free_weekday_to_every_day_and_drops_rest_days()
    {
        var draft = RoutineDraft.Empty();
        draft.Days.Add(new DraftDay { IsRest = true });
        draft.Days.Add(new DraftDay());

        draft.SetMode(RoutineDraft.WeeklyMode);

        Assert.Equal(2, draft.Days.Count);
        Assert.Equal([1, 2], draft.Days.Select(d => d.DayOfWeek!.Value));
        Assert.All(draft.Days, d => Assert.False(d.IsRest));
        Assert.Equal("El nombre es obligatorio.", draft.Validate());
    }

    [Fact]
    public void Weekly_form_shows_a_weekday_select_per_day_and_no_rest_switch()
    {
        var cut = Render(WeeklyDraft());

        Assert.Single(cut.FindAll("[data-testid='weekday-0']"));
        Assert.Single(cut.FindAll("[data-testid='weekday-1']"));
        Assert.DoesNotContain("Descanso", cut.Markup.Replace("Descanso (s)", string.Empty));
    }

    [Fact]
    public void Picking_a_weekday_updates_the_draft_and_sends_it_in_the_request()
    {
        var draft = WeeklyDraft();
        var cut = Render(draft);

        cut.Find("select[data-testid='weekday-1']").Change("5");

        Assert.Equal(5, draft.Days[1].DayOfWeek);
        var exercise = new ExerciseResponse { Id = Guid.NewGuid(), Name = "Sentadilla" };
        foreach (var day in draft.Days)
        {
            day.Exercises = [new DraftExercise { Exercise = exercise, Sets = [new DraftSet { RepsMin = 8 }] }];
        }

        Assert.Null(draft.Validate());
        var request = draft.ToRequest();
        Assert.Equal("weekly", request.ScheduleMode);
        Assert.Equal([1, 5], request.Days.Select(d => d.DayOfWeek!.Value));
    }

    [Fact]
    public void A_weekday_already_taken_by_another_day_cannot_be_picked()
    {
        var draft = WeeklyDraft(); // lunes y martes
        var cut = Render(draft);

        var options = cut.Find("select[data-testid='weekday-1']").QuerySelectorAll("option");
        var monday = options.Single(o => o.GetAttribute("value") == "1");
        var tuesday = options.Single(o => o.GetAttribute("value") == "2");
        Assert.True(monday.HasAttribute("disabled"));
        Assert.False(tuesday.HasAttribute("disabled"));

        // Aunque se fuerce, un borrador con un día repetido no se puede enviar.
        draft.Days[1].DayOfWeek = 1;
        Assert.NotNull(draft.Validate());
    }

    [Fact]
    public void Weekly_cannot_have_more_than_seven_days()
    {
        var draft = RoutineDraft.Empty();
        draft.SetMode(RoutineDraft.WeeklyMode);
        for (var i = 0; i < 6; i++)
        {
            draft.AddDay();
        }

        Assert.Equal(7, draft.Days.Count);
        Assert.False(draft.CanAddDay);
        Assert.Equal(Enumerable.Range(1, 7), draft.Days.Select(d => d.DayOfWeek!.Value));
    }

    [Fact]
    public void Changing_the_mode_of_an_existing_routine_warns_that_a_new_one_is_created()
    {
        var routine = new RoutineResponse
        {
            Name = "Ciclo",
            ScheduleMode = "rotating",
            Days = [new RoutineDayResponse { Id = Guid.NewGuid(), Name = "D1" }],
        };
        var draft = RoutineDraft.From(routine);
        var cut = Render(draft);
        Assert.DoesNotContain("crea una rutina nueva", cut.Markup);

        cut.Find("select[data-testid='schedule-mode']").Change("weekly");

        Assert.True(draft.ModeChanged);
        Assert.Contains("crea una rutina nueva", cut.Markup);
        Assert.Equal(1, draft.Days[0].DayOfWeek);
    }

    [Fact]
    public void Editing_a_weekly_routine_loads_its_weekdays()
    {
        var routine = new RoutineResponse
        {
            Name = "Semanal",
            ScheduleMode = "weekly",
            Days =
            [
                new RoutineDayResponse { Id = Guid.NewGuid(), Name = "Empuje", DayOfWeek = 1 },
                new RoutineDayResponse { Id = Guid.NewGuid(), Name = "Pierna", DayOfWeek = 5 },
            ],
        };

        var draft = RoutineDraft.From(routine);

        Assert.True(draft.IsWeekly);
        Assert.False(draft.ModeChanged);
        Assert.Equal([1, 5], draft.ToRequest().Days.Select(d => d.DayOfWeek!.Value));
    }
}
