using System.Reflection;
using Bunit;
using GSM.Client.Http;
using GSM.Client.Generated;
using GSM.Member.UI.Pages;
using GSM.Panel.UI.Pages.Settings;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>G2 — pestaña "Datos del gym" con los grupos nuevos y la página "Mi gimnasio" del miembro.</summary>
public class GymProfileUiTests : TestContext
{
    /// <summary>Cliente de mentira: devuelve el valor configurado para la operación pedida y recuerda las escrituras.</summary>
    public class ClientStub : DispatchProxy
    {
        public GymResponse? Gym { get; set; }
        public MemberGymResponse? MemberGym { get; set; }
        public List<UpdateGymRequest> Saved { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name == "GymPUTAsync" && args![0] is UpdateGymRequest request)
            {
                Saved.Add(request);
            }

            object? result = name switch
            {
                "GymGETAsync" or "GymPUTAsync" => Gym,
                "GymGET2Async" => MemberGym,
                _ => null,
            };

            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            var inner = returnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }
    }

    private ClientStub RegisterPanel(GymResponse gym)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var gyms = DispatchProxy.Create<IGymClient, ClientStub>();
        var stub = (ClientStub)(object)gyms;
        stub.Gym = gym;
        Services.AddSingleton(gyms);
        Services.AddSingleton(DispatchProxy.Create<IAuthClient, ClientStub>());
        var store = new InMemoryTokenStore();
        Services.AddSingleton<ITokenStore>(store);
        Services.AddSingleton(new GsmAuthenticationStateProvider(store));
        Services.AddSingleton<GymContext>();
        Services.AddSingleton<SessionService>();
        return stub;
    }

    private static GymResponse Gym(string? address = "Calle 10 # 5-20", string? city = "Medellín") => new()
    {
        Id = Guid.NewGuid(),
        Name = "Iron Perfil",
        Slug = "iron-perfil",
        Phone = "+573001234567",
        PrimaryColor = "#B50016",
        SecondaryColor = "#0AAFCA",
        LegalName = "Iron Perfil S.A.S.",
        Address = address,
        City = city,
        Latitude = 6.2442,
        Longitude = -75.5812,
        GeofenceRadiusMeters = 200,
        InstagramUrl = "https://instagram.com/ironperfil",
        WhatsAppPhone = "+573007654321",
    };

    [Fact]
    public void Gym_data_tab_shows_the_new_groups_and_a_map_link()
    {
        RegisterPanel(Gym());

        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<GymDataTab>();

        cut.WaitForAssertion(() => Assert.Contains("Razón social", cut.Markup));
        Assert.Contains("Ubicación", cut.Markup);
        Assert.Contains("Dirección", cut.Markup);
        Assert.Contains("Ciudad", cut.Markup);
        Assert.Contains("Latitud", cut.Markup);
        Assert.Contains("Radio de la geocerca", cut.Markup);
        Assert.Contains("WhatsApp", cut.Markup);
        Assert.Contains("Instagram", cut.Markup);
        Assert.Contains("TikTok", cut.Markup);
        Assert.Equal(
            "https://www.google.com/maps/search/?api=1&query=6.2442,-75.5812",
            cut.Find("#gym-map-link").GetAttribute("href"));
    }

    [Fact]
    public void Gym_data_tab_blocks_saving_without_address_or_city()
    {
        RegisterPanel(Gym(address: null, city: null));

        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<GymDataTab>();

        // Sin dirección no se guarda, pero la pantalla no abre en rojo: lo explica con una nota neutra.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='gym-location-missing']")));
        Assert.DoesNotContain("La dirección es obligatoria.", cut.Markup);
        Assert.True(cut.Find("#gym-save").HasAttribute("disabled"));
    }

    [Fact]
    public void Gym_data_tab_saves_the_new_fields()
    {
        var stub = RegisterPanel(Gym());

        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<GymDataTab>();
        cut.WaitForAssertion(() => Assert.False(cut.Find("#gym-save").HasAttribute("disabled")));
        cut.Find("#gym-save").Click();

        cut.WaitForAssertion(() => Assert.Single(stub.Saved));
        var saved = stub.Saved[0];
        Assert.Equal("Calle 10 # 5-20", saved.Address);
        Assert.Equal("Medellín", saved.City);
        Assert.Equal(6.2442, saved.Latitude);
        Assert.Equal(200, saved.GeofenceRadiusMeters);
        Assert.Equal("+573007654321", saved.WhatsAppPhone);
        Assert.Equal("Iron Perfil S.A.S.", saved.LegalName);
    }

    [Fact]
    public void My_gym_page_offers_directions_and_whatsapp()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var me = DispatchProxy.Create<IMeClient, ClientStub>();
        ((ClientStub)(object)me).MemberGym = new MemberGymResponse
        {
            Name = "Iron Perfil",
            PrimaryColor = "#B50016",
            SecondaryColor = "#0AAFCA",
            Address = "Calle 10 # 5-20",
            City = "Medellín",
            Latitude = 6.2442,
            Longitude = -75.5812,
            Phone = "+573001234567",
            WhatsAppPhone = "+573007654321",
            InstagramUrl = "https://instagram.com/ironperfil",
        };
        Services.AddSingleton(me);

        var cut = RenderComponent<MyGym>();

        cut.WaitForAssertion(() => Assert.Contains("Iron Perfil", cut.Markup));
        Assert.Contains("Calle 10 # 5-20, Medellín", cut.Find("#gym-address").TextContent);
        Assert.Equal(
            "https://www.google.com/maps/dir/?api=1&destination=6.2442%2C-75.5812",
            cut.Find("#gym-directions").GetAttribute("href"));
        Assert.Equal("https://wa.me/573007654321", cut.Find("#gym-whatsapp").GetAttribute("href"));
        Assert.Equal("tel:+573001234567", cut.Find("#gym-call").GetAttribute("href"));
        Assert.Contains("Instagram", cut.Markup);
        Assert.DoesNotContain("Facebook", cut.Markup);
    }

    [Fact]
    public void My_gym_page_hides_what_the_gym_never_filled_in()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var me = DispatchProxy.Create<IMeClient, ClientStub>();
        ((ClientStub)(object)me).MemberGym = new MemberGymResponse
        {
            Name = "Gym Vacío",
            PrimaryColor = "#B50016",
            SecondaryColor = "#0AAFCA",
        };
        Services.AddSingleton(me);

        var cut = RenderComponent<MyGym>();

        cut.WaitForAssertion(() => Assert.Contains("Gym Vacío", cut.Markup));
        Assert.DoesNotContain("Cómo llegar", cut.Markup);
        Assert.DoesNotContain("WhatsApp", cut.Markup);
        Assert.DoesNotContain("Redes", cut.Markup);
    }

    [Fact]
    public void Gym_links_fall_back_to_the_address_and_skip_empty_data()
    {
        Assert.Equal(
            "https://www.google.com/maps/dir/?api=1&destination=Calle%2010%20%23%205-20%2C%20Cali",
            GymLinks.DirectionsUrl(null, null, "Calle 10 # 5-20", "Cali"));
        Assert.Null(GymLinks.DirectionsUrl(null, null, null, " "));
        Assert.Null(GymLinks.MapUrl(6.2, null, null, null));
        Assert.Equal("https://wa.me/573001234567", GymLinks.WhatsAppUrl("+573001234567"));
        Assert.Null(GymLinks.WhatsAppUrl(null));
    }
}
