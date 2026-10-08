using Bunit;
using GSM.Client.Generated;
using GSM.Web.Pages.Admin;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

public class MonthlyMetricsPanelTests : TestContext
{
    private sealed class FakeMetricsClient(IReadOnlyList<MonthlyMetricItem> months) : IAdminMetricsClient
    {
        public Task<MonthlyMetricsResponse> MonthlyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MonthlyMetricsResponse { Months = months.ToList() });
    }

    private static MonthlyMetricItem Month(DateOnly month, double mrr, double? churn) => new()
    {
        Month = month,
        Mrr = mrr,
        Collected = mrr / 2,
        NewGyms = 1,
        ConvertedGyms = 1,
        LostGyms = 1,
        ActiveGymsAtStart = 2,
        ActiveGymsAtClose = 3,
        Churn = churn,
        ActiveMembers = 40,
        CheckIns = 500,
    };

    private IRenderedComponent<MonthlyMetricsPanel> Render(params MonthlyMetricItem[] months)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IAdminMetricsClient>(new FakeMetricsClient(months));
        return RenderComponent<MonthlyMetricsPanel>();
    }

    [Fact]
    public void The_panel_shows_one_row_per_month_with_the_series()
    {
        var cut = Render(Month(new DateOnly(2026, 8, 1), 100_000, 0.25), Month(new DateOnly(2026, 9, 1), 180_000, 0.5));

        var rows = cut.FindAll("[data-testid='metrics-table'] tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains(MonthlyMetricsPanel.MonthLabel(new DateOnly(2026, 8, 1)), rows[0].TextContent);
        Assert.Contains(MonthlyMetricsPanel.MonthLabel(new DateOnly(2026, 9, 1)), rows[1].TextContent);
        Assert.Contains("50,0 %", rows[1].TextContent);
        Assert.Contains("180.000", rows[1].TextContent);
    }

    [Fact]
    public void A_month_without_active_gyms_at_the_start_shows_a_dash_instead_of_churn()
    {
        var cut = Render(Month(new DateOnly(2026, 9, 1), 0, null));

        Assert.Equal("—", MonthlyMetricsPanel.ChurnLabel(null));
        Assert.DoesNotContain("0,0 %", cut.Find("[data-testid='metrics-table']").TextContent);
    }

    [Fact]
    public void Without_closed_months_the_panel_explains_when_the_first_one_arrives()
    {
        var cut = Render();

        Assert.Contains("primera ejecución del mes siguiente", cut.Find("[data-testid='metrics-empty']").TextContent);
    }
}
