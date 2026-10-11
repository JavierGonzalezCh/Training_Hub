using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.Member.UI.Pages;
using GSM.Panel.UI.Pages.Settings;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>G3 — editor del horario de atención (validación de tramos) y el indicador abierto/cerrado de la app.</summary>
public class OpeningHoursUiTests : TestContext
{
    /// <summary>Cliente de mentira: devuelve lo configurado por operación y recuerda cada llamada.</summary>
    public class ClientStub : DispatchProxy
    {
        public Dictionary<string, object?> Results { get; } = [];
        public List<(string Name, object?[] Args)> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            Calls.Add((name, args ?? []));
            Results.TryGetValue(name, out var result);

            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            var inner = returnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }
    }

    private static List<OpeningDayDto> Week(Action<List<OpeningDayDto>>? tweak = null)
    {
        OpeningDayDto D(string day, string a, string b) => new()
        {
            Day = day,
            Slots = [new TimeSlotDto { Opens = a, Closes = b }],
        };

        var days = new List<OpeningDayDto>
        {
            D("Monday", "05:00", "22:00"), D("Tuesday", "05:00", "22:00"), D("Wednesday", "05:00", "22:00"),
            D("Thursday", "05:00", "22:00"), D("Friday", "05:00", "22:00"), D("Saturday", "07:00", "14:00"),
            D("Sunday", "07:00", "13:00"), D("Holiday", "07:00", "13:00"),
        };
        tweak?.Invoke(days);
        return days;
    }

    private static GymOpenStatusDto Open() => new()
    {
        HasSchedule = true, IsOpen = true, ClosesAt = "22:00", Text = "Abierto · cierra a las 22:00",
    };

    private static GymOpenStatusDto Closed() => new()
    {
        HasSchedule = true, IsOpen = false, NextOpensAt = "05:00", NextOpensOn = new DateOnly(2026, 10, 6),
        Text = "Cerrado · abre mañana a las 5:00",
    };

    private ClientStub RegisterTab(OpeningHoursResponse hours, params HolidayResponse[] holidays)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var gym = DispatchProxy.Create<IGymClient, ClientStub>();
        var stub = (ClientStub)(object)gym;
        stub.Results["OpeningHoursGETAsync"] = hours;
        stub.Results["OpeningHoursPUTAsync"] = hours;
        stub.Results["HolidaysAsync"] = holidays.ToList();
        stub.Results["SpecialDaysAllAsync"] = new List<SpecialDayResponse>();
        Services.AddSingleton(gym);
        return stub;
    }

    // ---------- Validación de tramos (sin renderizar) ----------

    [Fact]
    public void Draft_requires_one_slot_per_day_and_orders_and_separates_them()
    {
        Assert.NotNull(OpeningHoursDraft.ValidateSlots([]));
        Assert.NotNull(OpeningHoursDraft.ValidateSlots([new SlotDraft("12:00", "05:00")]));
        Assert.NotNull(OpeningHoursDraft.ValidateSlots([new SlotDraft("", "05:00")]));
        Assert.NotNull(OpeningHoursDraft.ValidateSlots([new SlotDraft("05:00", "12:00"), new SlotDraft("11:00", "15:00")]));
        Assert.Null(OpeningHoursDraft.ValidateSlots([new SlotDraft("15:00", "22:00"), new SlotDraft("05:00", "12:00")]));
    }

    [Fact]
    public void Template_is_valid_and_covers_the_seven_days_plus_holidays()
    {
        var draft = OpeningHoursDraft.Template();

        Assert.True(draft.IsValid);
        Assert.Equal(8, draft.Days.Count);
        Assert.Equal(8, draft.ToRequest().Days.Count);
        Assert.Equal("07:00", draft.Days.Single(d => d.Day == "Holiday").Slots[0].Opens);
    }

    // ---------- Editor ----------

    [Fact]
    public void Tab_shows_the_week_the_holidays_and_saves_the_whole_schedule()
    {
        var stub = RegisterTab(
            new OpeningHoursResponse { HasSchedule = true, Days = Week(), Status = Open() },
            new HolidayResponse
            {
                Date = new DateOnly(2026, 10, 12), Name = "Día de la Raza", IsAdjusted = false, IsClosed = false,
                Slots = [new TimeSlotDto { Opens = "07:00", Closes = "13:00" }],
            });

        var cut = RenderComponent<OpeningHoursTab>();

        cut.WaitForAssertion(() => Assert.Contains("Festivos de", cut.Markup));
        Assert.Contains("Día de la Raza", cut.Markup);
        Assert.Contains("7:00–13:00", cut.Markup);
        Assert.Contains("Ahora: Abierto · cierra a las 22:00", cut.Find("#hours-status").TextContent);
        Assert.False(cut.Find("#hours-save").HasAttribute("disabled"));
        Assert.DoesNotContain("Empezar con una plantilla", cut.Markup);

        cut.Find("#hours-save").Click();

        cut.WaitForAssertion(() => Assert.Contains(stub.Calls, c => c.Name == "OpeningHoursPUTAsync"));
        var request = (UpdateOpeningHoursRequest)stub.Calls.Single(c => c.Name == "OpeningHoursPUTAsync").Args[0]!;
        Assert.Equal(8, request.Days.Count);
        Assert.Equal("05:00", request.Days.First(d => d.Day == "Monday").Slots.First().Opens);
    }

    [Fact]
    public void Tab_blocks_saving_when_slots_overlap_or_close_before_opening()
    {
        RegisterTab(new OpeningHoursResponse
        {
            HasSchedule = true,
            Status = Open(),
            Days = Week(days =>
            {
                days.Single(d => d.Day == "Monday").Slots =
                    [new TimeSlotDto { Opens = "05:00", Closes = "12:00" }, new TimeSlotDto { Opens = "11:00", Closes = "15:00" }];
                days.Single(d => d.Day == "Tuesday").Slots = [new TimeSlotDto { Opens = "22:00", Closes = "05:00" }];
            }),
        });

        var cut = RenderComponent<OpeningHoursTab>();

        cut.WaitForAssertion(() => Assert.Contains("Los tramos no pueden solaparse.", cut.Markup));
        Assert.Contains("Cada tramo debe abrir antes de cerrar.", cut.Markup);
        Assert.True(cut.Find("#hours-save").HasAttribute("disabled"));
    }

    [Fact]
    public void Tab_without_a_schedule_offers_the_template_and_then_allows_saving()
    {
        RegisterTab(new OpeningHoursResponse
        {
            HasSchedule = false,
            Days = [],
            Status = new GymOpenStatusDto { HasSchedule = false, IsOpen = true, Text = "Horario sin cargar" },
        });

        var cut = RenderComponent<OpeningHoursTab>();

        cut.WaitForAssertion(() => Assert.Contains("siempre abierto", cut.Markup));
        Assert.True(cut.Find("#hours-save").HasAttribute("disabled"));

        cut.Find("#hours-template").Click();

        cut.WaitForAssertion(() => Assert.False(cut.Find("#hours-save").HasAttribute("disabled")));
        Assert.DoesNotContain("Empezar con una plantilla", cut.Markup);
    }

    // ---------- Excepción: cerrar un día exige confirmar ----------

    [Fact]
    public async Task Closing_a_whole_day_asks_for_confirmation_before_enabling_save()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var gym = DispatchProxy.Create<IGymClient, ClientStub>();
        var stub = (ClientStub)(object)gym;
        Services.AddSingleton(gym);

        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<SpecialDayDialog>("Excepción", new DialogParameters<SpecialDayDialog>
        {
            { x => x.Date, new DateOnly(2026, 12, 24) },
            { x => x.Suggested, new List<TimeSlotDto> { new() { Opens = "06:00", Closes = "12:00" } } },
        }));

        // Con otro horario válido ya se puede guardar.
        Assert.False(provider.Find("#special-save").HasAttribute("disabled"));

        provider.Find("#special-mode-closed").Click();

        provider.WaitForAssertion(() => Assert.Contains("¿Seguro? Ese día nadie podrá entrar y las clases se cancelan.", provider.Markup));
        Assert.True(provider.Find("#special-save").HasAttribute("disabled"));

        provider.Find("#special-confirm").Change(true);

        provider.WaitForAssertion(() => Assert.False(provider.Find("#special-save").HasAttribute("disabled")));
        provider.Find("#special-save").Click();

        provider.WaitForAssertion(() => Assert.Contains(stub.Calls, c => c.Name == "SpecialDaysPUTAsync"));
        var body = (UpsertSpecialDayRequest)stub.Calls.Single(c => c.Name == "SpecialDaysPUTAsync").Args[1]!;
        Assert.True(body.IsClosed);
        Assert.True(body.ConfirmClosed);
    }

    // ---------- App del miembro ----------

    [Fact]
    public void Badge_says_when_the_gym_closes_or_when_it_opens_again()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(DispatchProxy.Create<IMeClient, ClientStub>());

        var open = RenderComponent<GymOpenBadge>(p => p.Add(x => x.Status, Open()));
        Assert.Equal("Abierto · cierra a las 22:00", open.Find("#gym-open-badge").TextContent);
        Assert.Contains("is-open", open.Find("#gym-open-badge").ClassName);

        var closed = RenderComponent<GymOpenBadge>(p => p.Add(x => x.Status, Closed()));
        Assert.Equal("Cerrado · abre mañana a las 5:00", closed.Find("#gym-open-badge").TextContent);
        Assert.Contains("is-closed", closed.Find("#gym-open-badge").ClassName);
    }

    [Fact]
    public void Badge_loads_by_itself_and_shows_nothing_for_a_gym_without_schedule()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var me = DispatchProxy.Create<IMeClient, ClientStub>();
        ((ClientStub)(object)me).Results["HoursAsync"] = new MemberGymHoursResponse { Status = Closed(), Days = Week(), Upcoming = [] };
        Services.AddSingleton(me);

        var loaded = RenderComponent<GymOpenBadge>();
        loaded.WaitForAssertion(() => Assert.Contains("Cerrado · abre mañana a las 5:00", loaded.Markup));

        ((ClientStub)(object)me).Results["HoursAsync"] = new MemberGymHoursResponse
        {
            Status = new GymOpenStatusDto { HasSchedule = false, IsOpen = true, Text = "Horario sin cargar" },
            Days = [], Upcoming = [],
        };
        var empty = RenderComponent<GymOpenBadge>();
        empty.WaitForAssertion(() => Assert.Empty(empty.FindAll("#gym-open-badge")));
    }

    [Fact]
    public void My_gym_page_lists_the_week_the_status_and_special_days()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var me = DispatchProxy.Create<IMeClient, ClientStub>();
        var stub = (ClientStub)(object)me;
        stub.Results["GymGET2Async"] = new MemberGymResponse { Name = "Iron Horario", PrimaryColor = "#B50016", SecondaryColor = "#0AAFCA" };
        stub.Results["HoursAsync"] = new MemberGymHoursResponse
        {
            Status = Open(),
            Days = Week(),
            Upcoming =
            [
                new SpecialDayResponse { Date = new DateOnly(2026, 12, 24), IsClosed = false, Slots = [new TimeSlotDto { Opens = "06:00", Closes = "12:00" }] },
                new SpecialDayResponse { Date = new DateOnly(2026, 12, 25), IsClosed = true, HolidayName = "Navidad", Slots = [] },
            ],
        };
        Services.AddSingleton(me);

        var cut = RenderComponent<MyGym>();

        cut.WaitForAssertion(() => Assert.Contains("Horario de atención", cut.Markup));
        Assert.Contains("Abierto · cierra a las 22:00", cut.Find("#gym-open-badge").TextContent);
        Assert.Contains("Lunes", cut.Find("#gym-hours").TextContent);
        Assert.Contains("Festivos", cut.Find("#gym-hours").TextContent);
        Assert.Contains("5:00–22:00", cut.Find("#gym-hours").TextContent);
        Assert.Contains("Cerrado", cut.Find("#gym-hours-special").TextContent);
        Assert.Contains("Navidad", cut.Find("#gym-hours-special").TextContent);
    }
}
