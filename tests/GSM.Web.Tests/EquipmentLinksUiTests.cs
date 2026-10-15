using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Member.UI.Pages;
using GSM.Panel.UI.Pages.Exercises;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Routines;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using ExercisesPage = GSM.Panel.UI.Pages.Exercises.Index;

namespace GSM.Web.Tests;

/// <summary>
/// E3: ligas ejercicio ↔ máquina en el panel, filtro "solo lo que se puede hacer en mi gym", aviso en el editor
/// de rutinas y foto/zona/instrucciones en la rutina del miembro.
/// </summary>
public class EquipmentLinksUiTests : TestContext
{
    private static readonly Guid ExerciseId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid PressId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid CableId = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid LineId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private static object Machine(string status = "Maintenance", string? photo = "https://img.test/prensa.jpg") => new
    {
        id = PressId,
        name = "Prensa 45°",
        type = "Machine",
        photoUrl = photo,
        zone = "Zona de pierna",
        instructions = "Asiento en el hueco 4",
        status,
        expectedBackOn = "2026-10-20",
    };

    private static object Line(bool available) => new
    {
        id = LineId,
        exerciseId = ExerciseId,
        exerciseName = "Prensa de piernas",
        order = 1,
        setTargets = new object[] { new { number = 1, setType = "normal", repsMin = 10, repsMax = 10 } },
        restSeconds = 60,
        trackingType = "RepsWeight",
        isUnilateral = false,
        muscleGroup = "LowerBody",
        equipment = new { isAvailable = available, machines = new[] { Machine(available ? "Available" : "Maintenance") } },
    };

    private sealed class FakeApi : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

        public bool ExerciseAvailable { get; set; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path, body));

            var status = HttpStatusCode.OK;
            string json;
            if (request.Method == HttpMethod.Put && path.Contains("/equipment-items"))
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            else if (path.StartsWith($"/api/exercises/{ExerciseId}/equipment"))
            {
                json = JsonSerializer.Serialize(new
                {
                    models = new[] { new { id = Guid.NewGuid(), name = "Prensa de piernas 45°", type = "Machine" } },
                    itemIds = new[] { PressId },
                    machines = new[] { Machine("Available") },
                    isAvailableHere = true,
                });
            }
            else if (path.StartsWith("/api/exercises"))
            {
                json = JsonSerializer.Serialize(new object[]
                {
                    new
                    {
                        id = ExerciseId, name = "Prensa de piernas", isSystem = true, equipment = new[] { "Machine" },
                        primaryMuscle = "Quadriceps", muscleGroup = "LowerBody", secondaryMuscles = Array.Empty<string>(),
                        exerciseType = "Strength", trackingType = "RepsWeight", difficulty = "Intermediate",
                        isAvailableHere = ExerciseAvailable, machines = new[] { Machine(ExerciseAvailable ? "Available" : "Maintenance") },
                    },
                });
            }
            else if (path.StartsWith("/api/equipment"))
            {
                json = JsonSerializer.Serialize(new object[]
                {
                    new { id = PressId, name = "Prensa 45°", type = "Machine", zone = "Zona de pierna", status = "Available", quantity = 1, isActive = true },
                    new { id = CableId, name = "Polea alta", type = "Cable", zone = "Espalda", status = "Maintenance", quantity = 1, isActive = true },
                });
            }
            else if (path.StartsWith("/api/me/routines/history"))
            {
                json = "[]";
            }
            else if (path.StartsWith("/api/me/routines"))
            {
                json = JsonSerializer.Serialize(new
                {
                    routine = new
                    {
                        id = Guid.NewGuid(), memberId = Guid.NewGuid(), name = "Rutina E3", assignedBy = Guid.NewGuid(),
                        startsOn = "2026-10-01", isActive = true, cycleLength = 1, scheduleMode = "rotating",
                        days = new[] { new { id = Guid.NewGuid(), position = 1, name = "D1", isRest = false, exercises = new[] { Line(false) } } },
                    },
                    today = new
                    {
                        date = "2026-10-15", position = 1, dayName = "D1", isRest = false, dayOfWeek = 4,
                        exercises = new[] { Line(false) },
                    },
                });
            }
            else
            {
                json = "[]";
            }

            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private readonly FakeApi _api = new();

    public EquipmentLinksUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var http = new HttpClient(_api) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton<IExercisesClient>(new ExercisesClient(http));
        Services.AddSingleton<IEquipmentClient>(new EquipmentClient(http));
        Services.AddSingleton<IMeClient>(new MeClient(http));
        Services.AddSingleton<IRoutineExercisesClient>(new RoutineExercisesClient(http));
        Services.AddSingleton<GymContext>();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("luis");
        auth.SetRoles("staff", "member");
    }

    // --- Textos del aviso ---

    [Fact]
    public void Notice_names_the_machine_its_state_and_return_date_only_when_the_exercise_cannot_be_done()
    {
        var maintenance = new ExerciseEquipmentInfo
        {
            IsAvailable = false,
            Machines = [new EquipmentMachineInfo { Name = "Prensa 45°", Status = "Maintenance", ExpectedBackOn = new DateOnly(2026, 10, 15) }],
        };
        Assert.Equal("Prensa 45° en mantenimiento hasta el 15 oct", EquipmentNotice.Warning(maintenance));

        var outOfService = new ExerciseEquipmentInfo
        {
            IsAvailable = false,
            Machines = [new EquipmentMachineInfo { Name = "Polea", Status = "OutOfService" }],
        };
        Assert.Equal("Polea fuera de servicio", EquipmentNotice.Warning(outOfService));

        var severalDown = new ExerciseEquipmentInfo
        {
            IsAvailable = false,
            Machines =
            [
                new EquipmentMachineInfo { Name = "Prensa 1", Status = "Maintenance" },
                new EquipmentMachineInfo { Name = "Prensa 2", Status = "OutOfService" },
            ],
        };
        Assert.Contains("Ninguna de las 2 máquinas", EquipmentNotice.Warning(severalDown));

        Assert.Contains("no tiene el equipo", EquipmentNotice.Warning(new ExerciseEquipmentInfo { IsAvailable = false, Machines = [] }));
        Assert.Null(EquipmentNotice.Warning(new ExerciseEquipmentInfo { IsAvailable = true, Machines = [new EquipmentMachineInfo { Name = "x", Status = "Available" }] }));
        Assert.Null(EquipmentNotice.Warning(null));
    }

    [Fact]
    public void Primary_machine_prefers_an_available_one()
    {
        var info = new ExerciseEquipmentInfo
        {
            IsAvailable = true,
            Machines =
            [
                new EquipmentMachineInfo { Name = "Rota", Status = "Maintenance" },
                new EquipmentMachineInfo { Name = "Buena", Status = "Available" },
            ],
        };

        Assert.Equal("Buena", EquipmentNotice.Primary(info)!.Name);
        Assert.Null(EquipmentNotice.Primary(null));
    }

    // --- Editor de rutinas ---

    [Fact]
    public void Editor_warns_on_a_saved_line_whose_machine_is_in_maintenance_without_blocking_the_save()
    {
        var draft = RoutineDraft.From(new RoutineResponse
        {
            Name = "Rutina",
            ScheduleMode = "rotating",
            Days =
            [
                new RoutineDayResponse
                {
                    Name = "D1",
                    Exercises =
                    [
                        new RoutineExerciseResponse
                        {
                            Id = LineId, ExerciseId = ExerciseId, ExerciseName = "Prensa de piernas",
                            SetTargets = [new SetTargetResponse { Number = 1, SetType = "normal", RepsMin = 10, RepsMax = 10 }],
                            Equipment = new ExerciseEquipmentInfo
                            {
                                IsAvailable = false,
                                Machines = [new EquipmentMachineInfo { Name = "Prensa 45°", Status = "Maintenance", ExpectedBackOn = new DateOnly(2026, 10, 15) }],
                            },
                        },
                    ],
                },
            ],
        });

        var cut = RenderComponent<RoutineForm>(p => p.Add(x => x.Draft, draft));

        var warning = cut.Find("[data-testid='equipment-warning-0-0']");
        Assert.Contains("Prensa 45° en mantenimiento hasta el 15 oct", warning.TextContent);
        Assert.Null(draft.Validate());           // no impide guardar
        Assert.Single(draft.ToRequest().Days);
    }

    [Fact]
    public void Editor_shows_no_warning_when_the_exercise_can_be_done()
    {
        var draft = RoutineDraft.Empty();
        draft.Name = "Rutina";
        draft.Days[0].Exercises.Add(new DraftExercise
        {
            Exercise = new ExerciseResponse { Id = ExerciseId, Name = "Prensa de piernas" },
            Equipment = new ExerciseEquipmentInfo { IsAvailable = true, Machines = [] },
        });

        var cut = RenderComponent<RoutineForm>(p => p.Add(x => x.Draft, draft));

        Assert.Empty(cut.FindAll("[data-testid='equipment-warning-0-0']"));
    }

    [Fact]
    public void Picking_an_exercise_that_cannot_be_done_today_carries_the_warning_into_the_line()
    {
        var line = new DraftExercise();

        line.Select(new ExerciseResponse
        {
            Id = ExerciseId, Name = "Remo con barra", IsAvailableHere = false, Machines = [],
        });
        Assert.Contains("no tiene el equipo", line.EquipmentWarning);

        line.Select(new ExerciseResponse { Id = ExerciseId, Name = "Flexiones", IsAvailableHere = true, Machines = [] });
        Assert.Null(line.EquipmentWarning);

        line.Select(null);
        Assert.Null(line.Equipment);
    }

    // --- Catálogo de ejercicios ---

    [Fact]
    public void Catalog_filters_by_availability_by_default_and_the_filter_can_be_turned_off()
    {
        var cut = RenderComponent<ExercisesPage>();

        var checkbox = cut.Find("input[data-testid='filter-here']");
        Assert.True(checkbox.HasAttribute("checked"));
        Assert.Contains(_api.Requests, r => r.Path.StartsWith("/api/exercises?")
            && r.Path.Contains("onlyAvailableHere=true", StringComparison.OrdinalIgnoreCase));

        checkbox.Change(false);

        Assert.Contains(_api.Requests, r => r.Path.Contains("onlyAvailableHere=false", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Catalog_marks_exercises_that_cannot_be_done_today_and_names_their_machines()
    {
        _api.ExerciseAvailable = false;

        var cut = RenderComponent<ExercisesPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='not-available-chip']")));
        Assert.Contains("Prensa 45°", cut.Find("[data-testid='machines']").TextContent);
    }

    [Fact]
    public void Catalog_hides_the_unavailable_chip_when_the_exercise_can_be_done()
    {
        var cut = RenderComponent<ExercisesPage>();

        cut.WaitForAssertion(() => Assert.Contains("Prensa de piernas", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid='not-available-chip']"));
    }

    // --- Diálogo de ligas ---

    [Fact]
    public async Task Link_dialog_shows_system_models_and_current_links_and_saves_the_selection()
    {
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ExerciseEquipmentDialog>
        {
            { x => x.ExerciseId, ExerciseId },
            { x => x.ExerciseName, "Prensa de piernas" },
        };
        await provider.InvokeAsync(() => dialogs.ShowAsync<ExerciseEquipmentDialog>("Máquinas del ejercicio", parameters));

        provider.WaitForAssertion(() =>
            Assert.Contains("Prensa de piernas 45°", provider.Find("[data-testid='linked-models']").TextContent));
        Assert.True(provider.Find($"input[data-testid='link-{PressId}']").HasAttribute("checked"));
        Assert.False(provider.Find($"input[data-testid='link-{CableId}']").HasAttribute("checked"));

        provider.Find($"input[data-testid='link-{CableId}']").Change(true);
        provider.Find("[data-testid='save-links']").Click();

        var put = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Put);
        Assert.Equal($"/api/exercises/{ExerciseId}/equipment-items", put.Path);
        Assert.Contains(PressId.ToString(), put.Body);
        Assert.Contains(CableId.ToString(), put.Body);
    }

    // --- App del miembro ---

    [Fact]
    public void Member_routine_shows_the_machine_photo_zone_instructions_and_the_maintenance_warning()
    {
        var cut = RenderComponent<Routine>();

        // Con la tarjeta cerrada ya se ve que hoy no se puede hacer.
        cut.WaitForAssertion(() =>
            Assert.Contains("Prensa 45° en mantenimiento hasta el 20 oct", cut.Find($"[data-testid='unavailable-{LineId}']").TextContent));

        cut.Find(".gsm-m-ex__head").Click();

        var equipment = cut.Find($"[data-testid='equipment-{LineId}']");
        Assert.Contains("Prensa 45°", equipment.QuerySelector("[data-testid='equipment-name']")!.TextContent);
        Assert.Contains("Zona de pierna", equipment.QuerySelector("[data-testid='equipment-zone']")!.TextContent);
        Assert.Contains("Asiento en el hueco 4", equipment.QuerySelector("[data-testid='equipment-instructions']")!.TextContent);
        Assert.Equal("https://img.test/prensa.jpg", equipment.QuerySelector("img.gsm-m-equip__photo")!.GetAttribute("src"));
        Assert.NotNull(equipment.QuerySelector("[data-testid='equipment-warning']"));
    }
}
