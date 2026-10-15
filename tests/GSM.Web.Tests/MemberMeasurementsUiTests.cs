using System.Reflection;
using Bunit;
using GSM.Client;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>M4 — la pestaña de mediciones de la ficha: bloqueo, alta, borrado en dos pasos y 403.</summary>
public class MemberMeasurementsUiTests : TestContext
{
    public class MembersStub : DispatchProxy
    {
        public MemberMeasurementsView View { get; set; } = new() { ConsentRequired = false, Items = [] };
        public int? ForbiddenStatus { get; set; }
        public List<AddMemberMeasurementRequest> Added { get; } = [];
        public List<Guid> Deleted { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result = null;
            switch (targetMethod!.Name)
            {
                case "ListMemberMeasurementsAsync":
                    if (ForbiddenStatus is { } status)
                    {
                        throw new GsmApiException("forbidden", status, string.Empty, new Dictionary<string, IEnumerable<string>>(), null);
                    }

                    result = View;
                    break;
                case "AddMemberMeasurementAsync":
                    var request = (AddMemberMeasurementRequest)args![1]!;
                    Added.Add(request);
                    var item = new MemberMeasurementItem
                    {
                        Id = Guid.NewGuid(),
                        MeasuredOn = request.MeasuredOn ?? DateOnly.FromDateTime(DateTime.Today),
                        WeightKg = request.WeightKg,
                        HeightCm = request.HeightCm,
                        EffectiveHeightCm = request.HeightCm,
                        RecordedByRole = "owner",
                    };
                    View = new MemberMeasurementsView { ConsentRequired = false, Items = [item, .. View.Items] };
                    result = item;
                    break;
                case "DeleteMemberMeasurementAsync":
                    var id = (Guid)args![1]!;
                    Deleted.Add(id);
                    View = new MemberMeasurementsView { ConsentRequired = false, Items = View.Items.Where(i => i.Id != id).ToList() };
                    return Task.CompletedTask;
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
    public void Without_the_data_authorization_the_tab_is_locked_and_offers_no_form()
    {
        var stub = Register();
        stub.View = new MemberMeasurementsView { ConsentRequired = true, Items = [] };

        var cut = RenderComponent<MemberMeasurementsPanel>(p => p.Add(x => x.MemberId, Guid.NewGuid()));

        Assert.Contains("autorización de tratamiento de datos", cut.Find(".gsm-measurements__consent-required").TextContent);
        Assert.Empty(cut.FindAll(".gsm-measurements__add"));
    }

    [Fact]
    public void A_403_shows_the_no_permission_message()
    {
        var stub = Register();
        stub.ForbiddenStatus = 403;

        var cut = RenderComponent<MemberMeasurementsPanel>(p => p.Add(x => x.MemberId, Guid.NewGuid()));

        Assert.Contains("No tienes permiso", cut.Find(".gsm-measurements__forbidden").TextContent);
        Assert.Empty(cut.FindAll(".gsm-measurements__add"));
    }

    [Fact]
    public void The_list_shows_weight_height_and_bmi()
    {
        var stub = Register();
        stub.View = new MemberMeasurementsView
        {
            ConsentRequired = false,
            Items =
            [
                new MemberMeasurementItem
                {
                    Id = Guid.NewGuid(), MeasuredOn = new DateOnly(2026, 10, 1), WeightKg = 80, HeightCm = 180,
                    EffectiveHeightCm = 180, Bmi = 24.7, RecordedByName = "Carlos", RecordedByRole = "staff",
                },
            ],
        };

        var cut = RenderComponent<MemberMeasurementsPanel>(p => p.Add(x => x.MemberId, Guid.NewGuid()));

        var row = cut.Find(".gsm-measurements__row").TextContent;
        Assert.Contains("80", row);
        Assert.Contains("180", row);
        Assert.Contains("24", row);
        Assert.Contains("Carlos", row);
    }

    [Fact]
    public void Adding_a_measurement_sends_the_weight_and_height_and_lists_it()
    {
        var stub = Register();
        var cut = RenderComponent<MemberMeasurementsPanel>(p => p.Add(x => x.MemberId, Guid.NewGuid()));

        Assert.Contains("Sin mediciones", cut.Markup);

        cut.Find(".gsm-measurements__weight input").Input("72,5");
        cut.Find(".gsm-measurements__height input").Input("175");
        cut.Find(".gsm-measurements__add").Click();

        var sent = Assert.Single(stub.Added);
        Assert.Equal(72.5, sent.WeightKg);
        Assert.Equal(175, sent.HeightCm);
        Assert.Null(sent.MeasuredOn);
        Assert.Single(cut.FindAll(".gsm-measurements__row"));
    }

    [Fact]
    public void Deleting_needs_a_second_confirming_click()
    {
        var stub = Register();
        var id = Guid.NewGuid();
        stub.View = new MemberMeasurementsView
        {
            ConsentRequired = false,
            Items = [new MemberMeasurementItem { Id = id, MeasuredOn = new DateOnly(2026, 10, 1), WeightKg = 70, RecordedByRole = "owner" }],
        };

        var cut = RenderComponent<MemberMeasurementsPanel>(p => p.Add(x => x.MemberId, Guid.NewGuid()));

        cut.Find(".gsm-measurements__delete").Click();
        Assert.Empty(stub.Deleted);

        cut.Find(".gsm-measurements__confirm-delete").Click();
        Assert.Equal(id, Assert.Single(stub.Deleted));
        Assert.Empty(cut.FindAll(".gsm-measurements__row"));
    }

    [Fact]
    public void The_history_labels_a_deleted_measurement()
    {
        Assert.Equal("Medición borrada", MemberLabels.FieldLabel("measurement"));
    }
}
