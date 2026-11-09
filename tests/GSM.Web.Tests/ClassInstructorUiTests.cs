using System.Reflection;
using System.Security.Claims;
using Bunit;
using GSM.UI.Shared.Auth;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Navigation.Modules;
using GSM.Panel.UI.Pages.Classes;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;
using ClassesIndex = GSM.Panel.UI.Pages.Classes.Index;

namespace GSM.Web.Tests;

/// <summary>C2: instructor de la lista o "Otro", clases sin entrenador activo y "Mis clases".</summary>
public class ClassInstructorUiTests : TestContext
{
    private readonly IClassesClient _client;
    private readonly FakeClient _fake;

    public ClassInstructorUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        this.AddTestAuthorization().SetAuthorized("ana");
        _client = DispatchProxy.Create<IClassesClient, FakeClient>();
        _fake = (FakeClient)(object)_client;
        _fake.Handlers["TrainersAsync"] = _ => new List<ClassInstructorOption>
        {
            new() { UserId = Guid.NewGuid(), FullName = "Coach Uno", IsOwner = false },
        };
        _fake.Handlers["OutsideHoursAsync"] = _ => new List<ClassOutsideHoursItem>();
        Services.AddSingleton(_client);
        // C6: el formulario de clase lee el inventario para ofrecer las máquinas del cupo.
        Services.AddSingleton(DispatchProxy.Create<IEquipmentClient, FakeClient>());
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public void A_class_whose_trainer_is_no_longer_active_is_flagged_in_the_list()
    {
        _fake.Handlers["ClassesAllAsync"] = _ => new List<ClassListItem>
        {
            new()
            {
                Id = Guid.NewGuid(), Name = "Spinning", IsActive = true, IsRecurring = true,
                StartTime = new TimeOnly(7, 0), DurationMinutes = 45, DaysOfWeek = ["Monday"],
                InstructorName = "Coach Uno", InstructorUserId = Guid.NewGuid(), NoActiveInstructor = true,
            },
            new()
            {
                Id = Guid.NewGuid(), Name = "Yoga", IsActive = true, IsRecurring = true,
                StartTime = new TimeOnly(8, 0), DurationMinutes = 45, DaysOfWeek = ["Tuesday"],
                InstructorName = "Invitada", NoActiveInstructor = false,
            },
        };
        RenderComponent<MudDialogProvider>();

        var page = RenderComponent<ClassesIndex>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".no-active-instructor-chip")));

        // Los botones de icono dicen qué hacen y sobre qué clase; la tabla se apila en el celular.
        var labels = page.FindAll("[data-testid='classes-table'] button[aria-label]").Select(b => b.GetAttribute("aria-label")).ToList();
        Assert.Contains("Editar Spinning", labels);
        Assert.Contains("Desactivar Yoga", labels);
        Assert.Contains("gsm-table--stack", page.Find("[data-testid='classes-table']").ClassName);
    }

    [Fact]
    public async Task New_class_form_offers_the_trainer_list_and_hides_the_free_text_until_Other()
    {
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<ClassFormDialog>("Nueva clase"));

        provider.WaitForAssertion(() => Assert.Single(provider.FindAll("#class-instructor")));
        Assert.Contains(_fake.Calls, c => c.Method == "TrainersAsync");
        Assert.Empty(provider.FindAll("input#class-instructor-other"));
    }

    [Fact]
    public async Task Editing_a_class_with_a_free_text_instructor_shows_it_under_Other()
    {
        var classId = Guid.NewGuid();
        _fake.Handlers["ClassesGETAsync"] = _ => new ClassResponse
        {
            Id = classId, Name = "Yoga", IsActive = true, IsRecurring = true,
            StartTime = new TimeOnly(8, 0), DurationMinutes = 45, DaysOfWeek = ["Tuesday"],
            InstructorName = "Invitada Externa",
        };
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<ClassFormDialog>(
            "Editar", new DialogParameters<ClassFormDialog> { { x => x.ClassId, classId } }));

        provider.WaitForAssertion(() =>
            Assert.Equal("Invitada Externa", provider.Find("input#class-instructor-other").GetAttribute("value")));
    }

    [Fact]
    public void My_classes_lists_the_entries_the_server_returns_and_has_an_empty_state()
    {
        var entry = new ScheduleEntry
        {
            ClassId = Guid.NewGuid(), ClassName = "Spinning", Status = "Scheduled",
            SessionDate = DateOnly.FromDateTime(DateTime.Today), StartTime = new TimeOnly(7, 0),
            DurationMinutes = 45, InstructorUserId = Guid.NewGuid(),
        };
        _fake.Handlers["MineAsync"] = _ => new ScheduleResponse { Entries = [entry] };
        RenderComponent<MudDialogProvider>();

        var page = RenderComponent<MyClasses>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".my-class-row")));
        Assert.Contains("Spinning", page.Markup);
        Assert.Contains(_fake.Calls, c => c.Method == "MineAsync");

        _fake.Handlers["MineAsync"] = _ => new ScheduleResponse { Entries = [] };
        var empty = RenderComponent<MyClasses>();
        empty.WaitForAssertion(() => Assert.Single(empty.FindAll(".my-classes-empty")));
    }

    [Fact]
    public void My_classes_is_no_longer_a_second_menu_entry_it_is_a_tab_inside_Classes()
    {
        Assert.DoesNotContain(new ClassesPanelModule().MenuEntries, e => e.Href == "/classes/mine");
    }

    [Fact]
    public void The_classes_page_offers_the_My_classes_tab_only_with_the_Trainer_permission_or_as_Owner()
    {
        Assert.True(User("staff", "trainer").HasPermission("trainer"));
        Assert.True(User("owner").HasPermission("trainer"));
        Assert.False(User("staff").HasPermission("trainer"));
    }

    private static ClaimsPrincipal User(string role, params string[] permissions)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
