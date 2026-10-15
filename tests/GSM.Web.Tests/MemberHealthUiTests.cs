using System.Reflection;
using Bunit;
using GSM.Client;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>M3 — la sección de salud de la ficha, el registro de consultas y los textos del historial.</summary>
public class MemberHealthUiTests : TestContext
{
    public class MembersStub : DispatchProxy
    {
        public MemberHealthView View { get; set; } = new() { ConsentRequired = false, Health = new MemberHealth() };
        public int? ForbiddenStatus { get; set; }
        public List<UpdateMemberHealthRequest> Saved { get; } = [];
        public List<MemberHealthAccessItem> Access { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result = null;
            switch (targetMethod!.Name)
            {
                case "GetMemberHealthAsync":
                    if (ForbiddenStatus is { } status)
                    {
                        throw new GsmApiException("forbidden", status, string.Empty, new Dictionary<string, IEnumerable<string>>(), null);
                    }

                    result = View;
                    break;
                case "UpdateMemberHealthAsync":
                    var request = (UpdateMemberHealthRequest)args![1]!;
                    Saved.Add(request);
                    result = new MemberHealth
                    {
                        MedicalConditions = request.MedicalConditions,
                        Medications = request.Medications,
                        TrainingRestrictions = request.TrainingRestrictions,
                        MedicalClearanceDate = request.MedicalClearanceDate,
                    };
                    break;
                case "ListMemberHealthAccessAsync":
                    result = new PagedResultOfMemberHealthAccessItem { Items = Access, Total = Access.Count, Page = 1, PageSize = 20 };
                    break;
            }

            var inner = targetMethod.ReturnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }
    }

    private MembersStub Register()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var client = DispatchProxy.Create<IMembersClient, MembersStub>();
        Services.AddSingleton(client);
        return (MembersStub)(object)client;
    }

    [Fact]
    public void Without_the_data_authorization_the_section_is_locked_and_offers_no_form()
    {
        var stub = Register();
        stub.View = new MemberHealthView { ConsentRequired = true, Health = null };

        var cut = RenderComponent<MemberHealthPanel>(p => p.Add(x => x.MemberId, Guid.NewGuid()));

        Assert.Contains("autorización de tratamiento de datos", cut.Find(".gsm-health__consent-required").TextContent);
        Assert.Empty(cut.FindAll(".gsm-health__save"));
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public void With_the_authorization_it_shows_the_saved_values_and_saving_sends_them()
    {
        var stub = Register();
        stub.View = new MemberHealthView
        {
            ConsentRequired = false,
            Health = new MemberHealth { MedicalConditions = "Asma", Medications = "Salbutamol", TrainingRestrictions = "Sin saltos" },
        };
        bool? alerts = null;

        var cut = RenderComponent<MemberHealthPanel>(p => p
            .Add(x => x.MemberId, Guid.NewGuid())
            .Add(x => x.OnSaved, (bool value) => alerts = value));

        var texts = cut.FindAll("textarea").Select(t => t.GetAttribute("value") ?? t.TextContent).ToList();
        Assert.Contains(texts, t => t.Contains("Asma"));
        Assert.Contains(texts, t => t.Contains("Sin saltos"));

        cut.Find(".gsm-health__conditions textarea").Input("Asma grave");
        cut.Find(".gsm-health__save").Click();

        var saved = Assert.Single(stub.Saved);
        Assert.Equal("Asma grave", saved.MedicalConditions);
        Assert.Equal("Salbutamol", saved.Medications);
        Assert.Equal("Sin saltos", saved.TrainingRestrictions);
        Assert.True(alerts);
    }

    [Fact]
    public void A_403_shows_the_no_permission_message_instead_of_the_form()
    {
        var stub = Register();
        stub.ForbiddenStatus = 403;

        var cut = RenderComponent<MemberHealthPanel>(p => p.Add(x => x.MemberId, Guid.NewGuid()));

        Assert.Contains("No tienes permiso", cut.Find(".gsm-health__forbidden").TextContent);
        Assert.Empty(cut.FindAll(".gsm-health__save"));
    }

    [Fact]
    public void The_access_panel_lists_who_viewed_with_their_role()
    {
        var stub = Register();
        stub.Access =
        [
            new MemberHealthAccessItem { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), UserName = "Carlos Coach", Role = "staff", AccessedAt = DateTimeOffset.UtcNow },
        ];

        var cut = RenderComponent<MemberHealthAccessPanel>(p => p.Add(x => x.MemberId, Guid.NewGuid()));

        Assert.Contains("Carlos Coach", cut.Markup);
        Assert.Contains("Staff", cut.Find(".gsm-health-access").TextContent);
    }

    [Fact]
    public void History_labels_name_the_health_fields()
    {
        Assert.Equal("Condiciones médicas", MemberLabels.FieldLabel("medical_conditions"));
        Assert.Equal("Medicamentos", MemberLabels.FieldLabel("medications"));
        Assert.Equal("Restricciones de entrenamiento", MemberLabels.FieldLabel("training_restrictions"));
        Assert.Equal("Fecha del apto médico", MemberLabels.FieldLabel("medical_clearance_date"));
    }
}
