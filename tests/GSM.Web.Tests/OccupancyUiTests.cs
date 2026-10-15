using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Member.UI.Components;
using GSM.Panel.UI.Pages.Attendance;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>A5: el dashboard del panel muestra personas y % del aforo; el miembro ve solo el nivel.</summary>
public class OccupancyUiTests : TestContext
{
    public OccupancyUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        this.AddTestAuthorization();
    }

    private FakeClient Register<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeClient>();
        Services.AddSingleton(proxy);
        return (FakeClient)(object)proxy;
    }

    [Fact]
    public void Panel_card_shows_people_and_percent_of_capacity()
    {
        var fake = Register<IAttendanceClient>();
        fake.Handlers["GetOccupancyAsync"] = _ => new OccupancyResponse
            { PeopleInside = 18, Capacity = 40, Percent = 45, Level = "moderate", AssumedStayMinutes = 120 };

        var cut = RenderComponent<OccupancyCard>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("18 personas", cut.Find("[data-testid='occupancy-card']").TextContent);
            Assert.Contains("45 % del aforo", cut.Find("[data-testid='occupancy-card']").TextContent);
        });
    }

    [Fact]
    public void Panel_card_without_capacity_shows_only_the_number()
    {
        var fake = Register<IAttendanceClient>();
        fake.Handlers["GetOccupancyAsync"] = _ => new OccupancyResponse
            { PeopleInside = 1, Capacity = null, Percent = null, Level = null, AssumedStayMinutes = 120 };

        var cut = RenderComponent<OccupancyCard>();

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("[data-testid='occupancy-card']").TextContent;
            Assert.Contains("1 persona", text);
            Assert.DoesNotContain("% del aforo", text);
            Assert.DoesNotContain("personas", text);
        });
    }

    [Fact]
    public void Panel_card_says_it_is_an_estimate()
    {
        var fake = Register<IAttendanceClient>();
        fake.Handlers["GetOccupancyAsync"] = _ => new OccupancyResponse { PeopleInside = 3, AssumedStayMinutes = 120 };

        var cut = RenderComponent<OccupancyCard>();

        cut.WaitForAssertion(() => Assert.Contains("estimación", cut.Find("[data-testid='occupancy-card']").TextContent));
    }

    [Theory]
    [InlineData("calm", "Tranquilo")]
    [InlineData("moderate", "Moderado")]
    [InlineData("full", "Lleno")]
    public void Member_card_shows_the_level_without_any_number(string level, string label)
    {
        var fake = Register<IMeClient>();
        fake.Handlers["GetMyOccupancyAsync"] = _ => new MemberOccupancyResponse { Level = level };

        var cut = RenderComponent<MemberOccupancyCard>();

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("[data-testid='member-occupancy']").TextContent;
            Assert.Contains("¿Qué tan lleno está?", text);
            Assert.Contains(label, text);
            Assert.DoesNotMatch(@"\d", text);
        });
    }

    [Fact]
    public void Member_card_is_hidden_when_the_gym_has_no_capacity()
    {
        var fake = Register<IMeClient>();
        fake.Handlers["GetMyOccupancyAsync"] = _ => new MemberOccupancyResponse { Level = null };

        var cut = RenderComponent<MemberOccupancyCard>();

        cut.WaitForState(() => cut.FindAll("[data-testid='member-occupancy']").Count == 0);
    }
}
