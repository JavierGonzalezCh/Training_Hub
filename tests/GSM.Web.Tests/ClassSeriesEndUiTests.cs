using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;
using ClassesIndex = GSM.Panel.UI.Pages.Classes.Index;

namespace GSM.Web.Tests;

/// <summary>C5: la lista de clases muestra hasta cuándo se repite una serie con fecha de fin.</summary>
public class ClassSeriesEndUiTests : TestContext
{
    [Fact]
    public void A_series_with_an_end_date_shows_until_when_it_repeats()
    {
        var page = RenderIndex(
        [
            new ClassListItem
            {
                Id = Guid.NewGuid(), Name = "Natación", IsActive = true, IsRecurring = true,
                StartTime = new TimeOnly(7, 0), DurationMinutes = 45, DaysOfWeek = ["Monday"],
                EndsOn = new DateOnly(2026, 12, 15),
            },
            new ClassListItem
            {
                Id = Guid.NewGuid(), Name = "Yoga", IsActive = true, IsRecurring = true,
                StartTime = new TimeOnly(8, 0), DurationMinutes = 45, DaysOfWeek = ["Tuesday"],
            },
        ]);

        page.WaitForAssertion(() =>
        {
            var marks = page.FindAll(".gsm-class-ends-on");
            Assert.Single(marks);
            Assert.Contains("hasta", marks[0].TextContent);
        });
    }

    private IRenderedComponent<ClassesIndex> RenderIndex(List<ClassListItem> classes)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        this.AddTestAuthorization().SetAuthorized("ana");
        var proxy = DispatchProxy.Create<IClassesClient, FakeClient>();
        var fake = (FakeClient)(object)proxy;
        fake.Handlers["ClassesAllAsync"] = _ => classes;
        fake.Handlers["OutsideHoursAsync"] = _ => new List<ClassOutsideHoursItem>();
        Services.AddSingleton(proxy);
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();
        return RenderComponent<ClassesIndex>();
    }
}
