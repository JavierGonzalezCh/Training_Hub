using System.Reflection;
using Bunit;
using GSM.Client;
using GSM.Client.Generated;
using GSM.Member.UI.Auth;
using GSM.Member.UI.Pages;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>M5 — "Mi perfil" del miembro: contacto, salud bloqueada o de solo lectura, y mediciones propias.</summary>
public class MyProfileUiTests : TestContext
{
    public class ProfileStub : DispatchProxy
    {
        public MyProfile Profile { get; set; } = Adult();
        public MemberMeasurementsView Measurements { get; set; } = new() { ConsentRequired = false, Items = [] };
        public List<UpdateMyProfileRequest> Updates { get; } = [];
        public List<AddMemberMeasurementRequest> Added { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result = null;
            switch (targetMethod!.Name)
            {
                case "GetMyProfileAsync":
                    result = Profile;
                    break;
                case "UpdateMyProfileAsync":
                    var update = (UpdateMyProfileRequest)args![0]!;
                    Updates.Add(update);
                    Profile.Phone = update.Phone;
                    result = Profile;
                    break;
                case "ListMyMeasurementsAsync":
                    result = Measurements;
                    break;
                case "AddMyMeasurementAsync":
                    var request = (AddMemberMeasurementRequest)args![0]!;
                    Added.Add(request);
                    var item = new MemberMeasurementItem
                    {
                        Id = Guid.NewGuid(),
                        MeasuredOn = new DateOnly(2026, 10, 1),
                        WeightKg = request.WeightKg,
                        HeightCm = request.HeightCm,
                        EffectiveHeightCm = request.HeightCm,
                        RecordedByRole = "member",
                    };
                    Measurements = new MemberMeasurementsView { ConsentRequired = false, Items = [item, .. Measurements.Items] };
                    result = item;
                    break;
            }

            var inner = targetMethod.ReturnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }
    }

    /// <summary>Responde con valores por defecto: solo sirve para que el bootstrap del contexto no falle.</summary>
    public class DefaultsStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var inner = targetMethod!.ReturnType.GetGenericArguments()[0];
            object? result = Activator.CreateInstance(inner);
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }
    }

    private static MyProfile Adult() => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "Ana",
        LastName = "Prueba",
        DocumentType = "cc",
        DocumentNumber = "123",
        Phone = "+573001112233",
        Email = "ana@test.local",
        IsMinor = false,
        HealthConsentRequired = false,
        CanEditHealth = true,
        Health = new MemberHealth { MedicalConditions = "Asma" },
    };

    private ProfileStub Register()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var profile = DispatchProxy.Create<IMeProfileClient, ProfileStub>();
        Services.AddSingleton(profile);
        Services.AddSingleton(new MemberContext());
        Services.AddSingleton(new MemberBootstrapService(
            DispatchProxy.Create<IMeClient, DefaultsStub>(),
            DispatchProxy.Create<IMembersClient, DefaultsStub>(),
            new MemberContext()));
        return (ProfileStub)(object)profile;
    }

    [Fact]
    public void The_profile_shows_identity_as_read_only_and_the_editable_contact()
    {
        Register();

        var cut = RenderComponent<Profile>();

        Assert.Contains("Ana Prueba", cut.Find(".gsm-profile__name").TextContent);
        Assert.Contains("ana@test.local", cut.Find(".gsm-profile__email").TextContent);
        Assert.Equal("+573001112233", cut.Find(".gsm-profile__phone input").GetAttribute("value"));
        // La identidad no tiene campos: solo texto.
        Assert.DoesNotContain(cut.FindAll("input"), i => i.GetAttribute("value") == "123");
    }

    [Fact]
    public void Saving_the_contact_sends_no_health_section()
    {
        var stub = Register();

        var cut = RenderComponent<Profile>();
        cut.Find(".gsm-profile__phone input").Input("3009998877");
        cut.Find(".gsm-profile__save-contact").Click();

        var sent = Assert.Single(stub.Updates);
        Assert.Equal("3009998877", sent.Phone);
        Assert.Null(sent.Health);
    }

    [Fact]
    public void The_goal_is_editable_and_saving_the_contact_sends_it()
    {
        var stub = Register();
        stub.Profile.Goal = "fitness";

        var cut = RenderComponent<Profile>();
        Assert.NotEmpty(cut.FindAll(".gsm-profile__goal"));
        cut.Find(".gsm-profile__save-contact").Click();

        Assert.Equal("fitness", Assert.Single(stub.Updates).Goal);
    }

    [Fact]
    public void Saving_the_health_section_sends_it_whole()
    {
        var stub = Register();

        var cut = RenderComponent<Profile>();
        cut.Find(".gsm-profile__conditions textarea").Input("Asma grave");
        cut.Find(".gsm-profile__save-health").Click();

        var sent = Assert.Single(stub.Updates);
        Assert.Equal("Asma grave", sent.Health!.MedicalConditions);
    }

    [Fact]
    public void Without_the_data_authorization_health_and_measurements_are_locked()
    {
        var stub = Register();
        stub.Profile = new MyProfile
        {
            Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", DocumentType = "cc", DocumentNumber = "1",
            HealthConsentRequired = true, CanEditHealth = false,
        };

        var cut = RenderComponent<Profile>();

        Assert.Contains("autorización de tratamiento de datos", cut.Find(".gsm-profile__health-locked").TextContent);
        Assert.Empty(cut.FindAll(".gsm-profile__save-health"));
        Assert.Contains("autorización", cut.Find(".gsm-profile__measurements-locked").TextContent);
        Assert.Empty(cut.FindAll(".gsm-profile__add-measurement"));
    }

    [Fact]
    public void A_minor_sees_health_read_only_and_cannot_add_measurements()
    {
        var stub = Register();
        stub.Profile = Adult();
        stub.Profile.IsMinor = true;
        stub.Profile.CanEditHealth = false;

        var cut = RenderComponent<Profile>();

        Assert.NotEmpty(cut.FindAll(".gsm-profile__health-minor"));
        Assert.Empty(cut.FindAll(".gsm-profile__save-health"));
        Assert.True(cut.Find(".gsm-profile__conditions textarea").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll(".gsm-profile__add-measurement"));
    }

    [Fact]
    public void Adding_a_measurement_sends_weight_and_optional_height_and_lists_it()
    {
        var stub = Register();

        var cut = RenderComponent<Profile>();
        cut.Find(".gsm-profile__weight input").Input("72,5");
        cut.Find(".gsm-profile__height input").Input("178");
        cut.Find(".gsm-profile__add-measurement").Click();

        var sent = Assert.Single(stub.Added);
        Assert.Equal(72.5, sent.WeightKg);
        Assert.Equal(178, sent.HeightCm);
        cut.WaitForAssertion(() => Assert.Contains("72", cut.Find(".gsm-profile__measurement-row").TextContent));
    }
}
