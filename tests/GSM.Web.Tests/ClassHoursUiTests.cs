using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Classes;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;
using ClassesIndex = GSM.Panel.UI.Pages.Classes.Index;

namespace GSM.Web.Tests;

/// <summary>C1: el panel avisa de las clases que quedaron fuera del horario del gym y marca las ocurrencias "Gym cerrado".</summary>
public class ClassHoursUiTests : TestContext
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    [Fact]
    public void The_class_list_warns_about_classes_outside_the_gym_hours()
    {
        var page = RenderIndex(
            classes: [new ClassListItem
            {
                Id = Guid.NewGuid(), Name = "Spinning", IsActive = true, IsRecurring = true,
                StartTime = new TimeOnly(7, 0), DurationMinutes = 45, DaysOfWeek = ["Monday"],
            }],
            outside: [new ClassOutsideHoursItem
            {
                ClassId = Guid.NewGuid(), Name = "Spinning", StartTime = new TimeOnly(7, 0), DurationMinutes = 45,
                IsRecurring = true, DaysOutside = ["Monday"],
            }]);

        page.WaitForAssertion(() =>
        {
            var alert = page.Find("#outside-hours");
            Assert.Contains("fuera del horario del gym", alert.TextContent);
            Assert.Contains("Spinning", alert.TextContent);
            Assert.Contains("Lunes", alert.TextContent);
        });
    }

    [Fact]
    public void Without_affected_classes_there_is_no_warning()
    {
        var page = RenderIndex(classes: [], outside: []);

        page.WaitForAssertion(() => Assert.Contains("No hay clases", page.Markup));
        Assert.Empty(page.FindAll("#outside-hours"));
    }

    [Fact]
    public void The_weekly_schedule_marks_an_occurrence_outside_the_hours_as_gym_closed()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IClassesClient, FakeClient>();
        var fake = (FakeClient)(object)proxy;
        fake.Handlers["ScheduleAsync"] = _ => new ScheduleResponse
        {
            From = Monday,
            To = Monday.AddDays(6),
            Entries =
            [
                new ScheduleEntry
                {
                    ClassId = Guid.NewGuid(), ClassName = "Spinning", SessionDate = Monday, StartTime = new TimeOnly(7, 0),
                    DurationMinutes = 45, Status = "Scheduled", OutsideOpeningHours = true,
                },
                new ScheduleEntry
                {
                    ClassId = Guid.NewGuid(), ClassName = "Yoga", SessionDate = Monday.AddDays(1), StartTime = new TimeOnly(8, 0),
                    DurationMinutes = 45, Status = "Scheduled", OutsideOpeningHours = false,
                },
            ],
        };
        Services.AddSingleton(proxy);
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();

        var page = RenderComponent<Schedule>();

        // La página arranca en la semana de hoy; se mueve a la del lunes sembrado.
        page.WaitForAssertion(() =>
        {
            Assert.Single(page.FindAll(".outside-hours-chip"));
            Assert.Contains("GYM CERRADO", page.Find(".outside-hours-chip").TextContent);
        });
    }

    private IRenderedComponent<ClassesIndex> RenderIndex(List<ClassListItem> classes, List<ClassOutsideHoursItem> outside)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IClassesClient, FakeClient>();
        var fake = (FakeClient)(object)proxy;
        fake.Handlers["ClassesAllAsync"] = _ => classes;
        fake.Handlers["OutsideHoursAsync"] = _ => outside;
        Services.AddSingleton(proxy);
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();
        return RenderComponent<ClassesIndex>();
    }
}
