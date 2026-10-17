using System.Reflection;
using System.Security.Claims;
using Bunit;
using GSM.Client;
using GSM.Client.Generated;
using GSM.UI.Shared.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using GSM.Panel.UI.Navigation.Modules;
using GSM.Panel.UI.Pages.Members;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;
using TemplatesIndex = GSM.Panel.UI.Pages.RoutineTemplates.Index;

namespace GSM.Web.Tests;

/// <summary>R11: lista de plantillas de rutina, entrada de menú y diálogo "Desde plantilla".</summary>
public class RoutineTemplateUiTests : TestContext
{
    private static (T Client, FakeClient Fake) Fake<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeClient>();
        return (proxy, (FakeClient)(object)proxy);
    }

    private static RoutineTemplateListItem Item(string name, string mode = "rotating", int days = 3, int exercises = 12)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Para principiantes",
            ScheduleMode = mode,
            DayCount = days,
            ExerciseCount = exercises,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void The_module_registers_the_templates_menu_entry()
    {
        var module = new RoutinesPanelModule();

        Assert.Contains(module.MenuEntries, e => e.Href == "/routine-templates");
    }

    [Fact]
    public void The_list_shows_each_template_with_its_calendar_mode_and_counts()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = Fake<IRoutineTemplatesClient>();
        fake.Handlers["ListRoutineTemplatesAsync"] = _ => new List<RoutineTemplateListItem>
        {
            Item("Fuerza 3 días"),
            Item("Semanal full body", "weekly", 2, 8),
        };
        Services.AddSingleton(client);
        RenderComponent<MudPopoverProvider>();

        var page = RenderComponent<TemplatesIndex>();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Fuerza 3 días", page.Markup);
            Assert.Contains("Semanal full body", page.Markup);
            Assert.Contains("Semanal", page.Markup);
            Assert.Contains("Rotativo", page.Markup);
        });
    }

    private IRenderedComponent<TemplatesIndex> RenderListAs(string role, params string[] permissions)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = Fake<IRoutineTemplatesClient>();
        fake.Handlers["ListRoutineTemplatesAsync"] = _ => new List<RoutineTemplateListItem> { Item("Fuerza 3 días") };
        Services.AddSingleton(client);
        RenderComponent<MudPopoverProvider>();

        var claims = new List<Claim> { new(ClaimTypes.Name, "ana"), new(ClaimTypes.Role, role) };
        claims.AddRange(permissions.Select(p => new Claim(GsmAuthenticationStateProvider.PermissionClaim, p)));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));
        return RenderComponent<TemplatesIndex>(p => p.AddCascadingValue(Task.FromResult(new AuthenticationState(user))));
    }

    [Fact]
    public void Only_the_owner_and_staff_with_the_trainer_permission_see_the_delete_button()
    {
        var plain = RenderListAs("staff");
        plain.WaitForAssertion(() => Assert.Contains("Fuerza 3 días", plain.Markup));
        Assert.Empty(plain.FindAll("[data-testid^='template-delete-']"));
        Assert.NotEmpty(plain.FindAll("[data-testid^='template-edit-']"));
    }

    [Fact]
    public void A_trainer_sees_the_delete_button()
    {
        var trainer = RenderListAs("staff", GsmPermissions.Trainer);
        trainer.WaitForAssertion(() => Assert.NotEmpty(trainer.FindAll("[data-testid^='template-delete-']")));
    }

    [Fact]
    public void The_owner_sees_the_delete_button()
    {
        var owner = RenderListAs("owner");
        owner.WaitForAssertion(() => Assert.NotEmpty(owner.FindAll("[data-testid^='template-delete-']")));
    }

    [Fact]
    public void The_list_explains_how_to_start_when_there_are_no_templates()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = Fake<IRoutineTemplatesClient>();
        fake.Handlers["ListRoutineTemplatesAsync"] = _ => new List<RoutineTemplateListItem>();
        Services.AddSingleton(client);
        RenderComponent<MudPopoverProvider>();

        var page = RenderComponent<TemplatesIndex>();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid='templates-empty']")));
    }

    [Fact]
    public async Task The_assign_dialog_says_so_when_the_gym_has_no_templates()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = Fake<IRoutineTemplatesClient>();
        fake.Handlers["ListRoutineTemplatesAsync"] = _ => new List<RoutineTemplateListItem>();
        Services.AddSingleton(client);
        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();

        await provider.InvokeAsync(() => dialogs.ShowAsync<AssignFromTemplateDialog>(
            "Asignar desde plantilla", new DialogParameters<AssignFromTemplateDialog> { { x => x.MemberId, Guid.NewGuid() } }));

        provider.WaitForAssertion(() =>
        {
            Assert.NotEmpty(provider.FindAll("[data-testid='assign-template-empty']"));
            Assert.DoesNotContain(fake.Calls, c => c.Method == "AssignRoutineTemplateAsync");
        });
    }
}
