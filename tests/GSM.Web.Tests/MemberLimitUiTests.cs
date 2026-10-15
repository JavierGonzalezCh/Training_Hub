using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Layout;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>S2 — indicador de uso del límite de miembros en el panel del gym y en la cartera.</summary>
public class MemberLimitUiTests : TestContext
{
    public class ClientStub : DispatchProxy
    {
        public MemberUsageResponse? Usage { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod!.ReturnType;
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            var inner = returnType.GetGenericArguments()[0];
            var result = targetMethod.Name == "MemberUsageAsync" ? Usage : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }
    }

    private IRenderedComponent<MemberLimitCard> Render(int? counted, int? limit)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var gyms = DispatchProxy.Create<IGymClient, ClientStub>();
        ((ClientStub)(object)gyms).Usage = new MemberUsageResponse { Counted = counted ?? 0, Limit = limit, PlanName = "Basic" };
        Services.AddSingleton(gyms);
        return RenderComponent<MemberLimitCard>();
    }

    [Fact]
    public void The_card_shows_the_usage_of_the_limit()
    {
        var cut = Render(142, 150);

        Assert.Contains("142 / 150", cut.Find("[data-testid='member-limit']").TextContent);
        Assert.Contains("Te quedan 8 cupos", cut.Markup);
    }

    [Fact]
    public void At_the_limit_the_card_says_so()
    {
        var cut = Render(150, 150);

        Assert.Contains("Llegaste al límite", cut.Markup);
    }

    [Fact]
    public void A_plan_without_limit_shows_nothing()
    {
        var cut = Render(40, null);

        Assert.Empty(cut.FindAll("[data-testid='member-limit']"));
    }

    [Theory]
    [InlineData(134, 150, MemberLimitCard.LimitState.Ok)]
    [InlineData(135, 150, MemberLimitCard.LimitState.Near)]   // 90 %
    [InlineData(149, 150, MemberLimitCard.LimitState.Near)]
    [InlineData(150, 150, MemberLimitCard.LimitState.Full)]
    public void The_warning_starts_at_ninety_percent(int counted, int limit, MemberLimitCard.LimitState expected)
        => Assert.Equal(expected, MemberLimitCard.State(counted, limit));

    [Fact]
    public void The_portfolio_labels_show_usage_with_or_without_limit()
    {
        Assert.Equal("142 / 150", SuperAdminLabels.MemberUsage(142, 150));
        Assert.Equal("12 / sin límite", SuperAdminLabels.MemberUsage(12, null));
        Assert.Null(SuperAdminLabels.MemberUsageRatio(12, null));
        Assert.Equal(1.0, SuperAdminLabels.MemberUsageRatio(200, 150));
        Assert.Equal(1, SuperAdminLabels.LegacyPlanValue("Pro"));
        Assert.Equal(0, SuperAdminLabels.LegacyPlanValue("Nuevo plan"));
    }
}
