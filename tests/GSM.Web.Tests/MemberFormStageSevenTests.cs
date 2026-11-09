using Bunit;
using GSM.Client.Generated;
using GSM.UI.Shared.Components;
using GSM.UI.Shared.Formatting;
using GSM.UI.Shared.Routines;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace GSM.Web.Tests;

/// <summary>Etapa 7 — celulares con indicativo (M2), fechas con año (F2) y cumplimiento con colores (F6).</summary>
public class MemberFormStageSevenTests : TestContext
{
    [Theory]
    [InlineData("+573001234567", "+57", "3001234567")]
    [InlineData("+14155550123", "+1", "4155550123")]
    [InlineData("+5933001234", "+593", "3001234")]
    [InlineData("", "+57", "")]
    [InlineData(null, "+57", "")]
    [InlineData("3001234567", "+57", "3001234567")]
    [InlineData("+999901234567", "+", "+999901234567")]
    public void A_stored_phone_is_split_into_country_code_and_number(string? stored, string code, string digits)
        => Assert.Equal((code, digits), PhoneField.Split(stored));

    [Theory]
    [InlineData("+57", "300 123 4567", "+573001234567")]
    [InlineData("+1", "4155550123", "+14155550123")]
    [InlineData("+", "998901234567", "+998901234567")]
    [InlineData("+57", "", null)]
    public void The_selected_code_and_the_digits_join_into_e164(string code, string digits, string? expected)
        => Assert.Equal(expected, PhoneField.Join(code, digits));

    [Theory]
    [InlineData("+573001234567", null)]
    [InlineData("+57300123456", "Un celular de Colombia tiene 10 dígitos y empieza por 3.")]
    [InlineData("+572001234567", "Un celular de Colombia tiene 10 dígitos y empieza por 3.")]
    [InlineData("+14155550123", null)]
    [InlineData("+1415", "El número debe tener entre 6 y 14 dígitos.")]
    [InlineData("", null)]
    public void Colombian_numbers_need_ten_digits_starting_with_three_and_others_six_to_fourteen(string phone, string? error)
        => Assert.Equal(error, PhoneField.Validate(phone));

    [Fact]
    public void The_phone_field_emits_e164_when_the_number_is_typed()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        RenderComponent<MudPopoverProvider>();
        string? emitted = null;

        var cut = RenderComponent<PhoneField>(p => p
            .Add(c => c.Value, null)
            .Add(c => c.ValueChanged, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<string?>(this, v => emitted = v)));
        cut.Find("input[data-testid='phone-number']").Input("3001234567");

        Assert.Equal("+573001234567", emitted);
    }

    [Fact]
    public void A_range_that_crosses_the_year_shows_the_year_on_both_ends()
    {
        var thisYear = GsmFormat.Today().Year;
        var start = new DateOnly(thisYear, 11, 8);
        var end = new DateOnly(thisYear + 1, 11, 7);

        Assert.Equal($"8 nov {thisYear}", GsmFormat.DayShort(start, end));
        Assert.Equal($"7 nov {thisYear + 1}", GsmFormat.DayShort(end, start));
        Assert.Equal("8 nov", GsmFormat.DayShort(new DateOnly(thisYear, 11, 8), new DateOnly(thisYear, 12, 8)));
        Assert.Equal($"8 nov {thisYear + 1}", GsmFormat.DayShort(new DateOnly(thisYear + 1, 11, 8)));
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData(10, "low")]
    [InlineData(49, "low")]
    [InlineData(50, "mid")]
    [InlineData(79, "mid")]
    [InlineData(80, "high")]
    [InlineData(100, "high")]
    public void Adherence_bands_follow_the_agreed_thresholds(int? percent, string band)
        => Assert.Equal(band, AdherenceLabels.Band(percent));

    [Fact]
    public void The_adherence_panel_hides_weeks_before_the_routine_colors_by_percent_and_explains_itself()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var monday = GsmFormat.Today();
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(-1);
        }

        AdherenceWeekItem Week(DateOnly start, int planned, int done, int? percent) => new()
        {
            WeekStart = start, PlannedDays = planned, DoneDays = done, DueDays = planned, CompliancePercent = percent,
            Days = new List<AdherenceDayItem>(),
        };
        var data = new MemberAdherence
        {
            HasRoutine = true,
            Weeks = new List<AdherenceWeekItem>
            {
                Week(monday.AddDays(-35), 0, 0, null),
                Week(monday.AddDays(-28), 3, 1, 33),
                Week(monday.AddDays(-21), 3, 2, 67),
                Week(monday.AddDays(-14), 3, 3, 100),
                Week(monday, 3, 1, 33),
            },
        };

        var cut = RenderComponent<AdherencePanel>(p => p.Add(c => c.Load, () => Task.FromResult(data)));

        var bars = cut.FindAll("[data-testid='adherence-week'] .gsm-adh-bar__fill");
        Assert.Equal(4, bars.Count);   // la semana anterior al inicio de la rutina no se muestra
        Assert.Equal(["low", "mid", "high", "current"], bars.Select(b => b.GetAttribute("data-band")).ToArray());
        Assert.Contains("Días entrenados de los que le tocaban según su rutina", cut.Find("[data-testid='adherence-caption']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='adherence-legend'] li"));
    }
}
