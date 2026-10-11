using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI.Auth;
using GSM.Member.UI.Pages;
using GSM.Panel.UI.Pages.Members;
using GSM.Panel.UI.Pages.Settings;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>M2 — sección de autorizaciones en la ficha, aceptación en la app y editor de textos.</summary>
public class ConsentsUiTests : TestContext
{
    private static MemberConsentStatus Status(
        string type, string status, int current = 0, int? accepted = null, string? channel = null, bool guardian = false) => new()
    {
        Type = type, Status = status, CurrentVersion = current, CurrentText = $"Texto de {type}",
        AcceptedVersion = accepted, Channel = channel, AcceptedByGuardian = guardian,
        AcceptedAt = accepted is null ? null : DateTimeOffset.UtcNow,
    };

    private sealed class FakeMemberConsents(List<MemberConsentStatus> statuses) : IMemberConsentsClient
    {
        public AcceptConsentRequest? Registered { get; private set; }
        public string? Revoked { get; private set; }

        public Task<ICollection<MemberConsentStatus>> ListMemberConsentsAsync(Guid memberId, CancellationToken cancellationToken = default)
            => Task.FromResult<ICollection<MemberConsentStatus>>(statuses);

        public Task<ICollection<MemberConsentStatus>> RegisterMemberConsentAsync(Guid memberId, AcceptConsentRequest body, CancellationToken cancellationToken = default)
        {
            Registered = body;
            statuses.RemoveAll(s => s.Type == body.Type);
            statuses.Add(Status(body.Type, "accepted", accepted: body.DocumentVersion, channel: "front_desk"));
            return Task.FromResult<ICollection<MemberConsentStatus>>(statuses);
        }

        public Task<ICollection<MemberConsentStatus>> RevokeMemberConsentAsync(Guid memberId, string type, CancellationToken cancellationToken = default)
        {
            Revoked = type;
            return Task.FromResult<ICollection<MemberConsentStatus>>(statuses);
        }
    }

    private sealed class FakeMeConsents(List<MemberConsentStatus> statuses) : IMeConsentsClient
    {
        public AcceptConsentRequest? Accepted { get; private set; }

        public Task<ICollection<MemberConsentStatus>> ListMyConsentsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ICollection<MemberConsentStatus>>(statuses);

        public Task<ICollection<MemberConsentStatus>> AcceptMyConsentAsync(AcceptConsentRequest body, CancellationToken cancellationToken = default)
        {
            Accepted = body;
            statuses.RemoveAll(s => s.Type == body.Type);
            statuses.Add(Status(body.Type, "accepted", accepted: body.DocumentVersion, channel: "app"));
            return Task.FromResult<ICollection<MemberConsentStatus>>(statuses);
        }

        public Task<ICollection<MemberConsentStatus>> RevokeMyConsentAsync(string type, CancellationToken cancellationToken = default)
            => Task.FromResult<ICollection<MemberConsentStatus>>(statuses);
    }

    private sealed class FakeDocuments(List<ConsentDocumentItem> docs) : IConsentDocumentsClient
    {
        public PublishConsentDocumentRequest? Published { get; private set; }

        public Task<ICollection<ConsentDocumentItem>> ListConsentDocumentsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ICollection<ConsentDocumentItem>>(docs);

        public Task<ConsentDocumentItem> PublishConsentDocumentAsync(PublishConsentDocumentRequest body, CancellationToken cancellationToken = default)
        {
            Published = body;
            return Task.FromResult(new ConsentDocumentItem
            {
                Type = body.Type, Version = 1, Text = body.Text, EffectiveFrom = DateTimeOffset.UtcNow, IsDefault = false,
            });
        }
    }

    private void UseMud()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    // --- Ficha del panel ---

    [Fact]
    public void Member_sheet_shows_each_authorization_state_with_version_channel_and_guardian()
    {
        UseMud();
        var api = new FakeMemberConsents(
        [
            Status(ConsentLabels.DataProcessing, "outdated", current: 2, accepted: 1, channel: "front_desk", guardian: true),
            Status(ConsentLabels.LiabilityWaiver, "not_accepted"),
        ]);
        Services.AddSingleton<IMemberConsentsClient>(api);

        var cut = RenderComponent<MemberConsentsPanel>(p => p.Add(x => x.MemberId, Guid.NewGuid()).Add(x => x.IsMinor, true));

        var data = cut.Find($"[data-consent={ConsentLabels.DataProcessing}]");
        Assert.Contains("Pendiente de aceptar la versión vigente", data.TextContent);
        Assert.Contains("versión 1", data.TextContent);
        Assert.Contains("Recepción", data.TextContent);
        Assert.Contains("aceptada por el acudiente", data.TextContent);
        Assert.Contains("Revocar", data.TextContent);

        var waiver = cut.Find($"[data-consent={ConsentLabels.LiabilityWaiver}]");
        Assert.Contains("Sin aceptar", waiver.TextContent);
        Assert.Empty(waiver.QuerySelectorAll(".gsm-consent__revoke"));
        Assert.Contains("menor de edad", cut.Markup);
    }

    [Fact]
    public async Task Staff_registers_the_paper_signature_after_confirming()
    {
        UseMud();
        var api = new FakeMemberConsents([Status(ConsentLabels.LiabilityWaiver, "not_accepted", current: 3)]);
        Services.AddSingleton<IMemberConsentsClient>(api);
        var provider = RenderComponent<MudDialogProvider>();
        var cut = RenderComponent<MemberConsentsPanel>(p => p.Add(x => x.MemberId, Guid.NewGuid()));

        // El clic queda esperando la respuesta del cuadro de confirmación: no se espera.
        _ = cut.Find(".gsm-consent__register").ClickAsync(new());
        await provider.WaitForElement(".mud-message-box__yes-button").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.NotNull(api.Registered));

        Assert.Equal(ConsentLabels.LiabilityWaiver, api.Registered!.Type);
        Assert.Equal(3, api.Registered.DocumentVersion);
        cut.WaitForAssertion(() => Assert.Contains("Aceptada", cut.Find(".gsm-consent__status").TextContent));
        Assert.Empty(cut.FindAll(".gsm-consent__register"));
    }

    // --- App del miembro ---

    [Fact]
    public async Task Member_app_lists_the_pending_ones_and_accepts_the_current_version()
    {
        UseMud();
        var api = new FakeMeConsents(
        [
            Status(ConsentLabels.DataProcessing, "not_accepted", current: 2),
            Status(ConsentLabels.LiabilityWaiver, "accepted", accepted: 0, channel: "app"),
        ]);
        Services.AddSingleton<IMeConsentsClient>(api);
        Services.AddSingleton(new MemberContext());

        var cut = RenderComponent<Consents>();

        Assert.Single(cut.FindAll(".gsm-m-consent__accept"));
        Assert.Contains("Texto de data_processing", cut.Markup);

        await cut.Find(".gsm-m-consent__accept").ClickAsync(new());

        Assert.Equal(2, api.Accepted!.DocumentVersion);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".gsm-m-consent__accept")));
    }

    [Fact]
    public void A_minor_sees_the_pending_ones_but_cannot_accept_them_in_the_app()
    {
        UseMud();
        Services.AddSingleton<IMeConsentsClient>(new FakeMeConsents([Status(ConsentLabels.DataProcessing, "not_accepted")]));
        var context = new MemberContext();
        context.Set(new MemberDetail { Id = Guid.NewGuid(), IsMinor = true, FirstName = "Kid", LastName = "Test" });
        Services.AddSingleton(context);

        var cut = RenderComponent<Consents>();

        Assert.Empty(cut.FindAll(".gsm-m-consent__accept"));
        Assert.Contains("tu acudiente", cut.Markup);
    }

    // --- Editor de textos en /settings ---

    [Fact]
    public async Task Owner_publishes_a_new_version_of_a_text_after_confirming()
    {
        UseMud();
        var api = new FakeDocuments(
        [
            new ConsentDocumentItem { Type = ConsentLabels.DataProcessing, Version = 0, Text = new string('a', 40), IsDefault = true },
            new ConsentDocumentItem { Type = ConsentLabels.LiabilityWaiver, Version = 2, Text = new string('b', 40), IsDefault = false, EffectiveFrom = DateTimeOffset.UtcNow },
        ]);
        Services.AddSingleton<IConsentDocumentsClient>(api);
        var provider = RenderComponent<MudDialogProvider>();
        var cut = RenderComponent<ConsentDocumentsTab>();

        Assert.Contains("borrador pendiente de revisión legal", cut.Markup);
        var buttons = cut.FindAll(".gsm-consent-doc__publish");
        Assert.Equal(2, buttons.Count);
        Assert.False(buttons[0].HasAttribute("disabled"));
        Assert.True(buttons[1].HasAttribute("disabled")); // sin cambios sobre una versión ya publicada

        _ = buttons[0].ClickAsync(new());
        await provider.WaitForElement(".mud-message-box__yes-button").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.NotNull(api.Published));

        Assert.Equal(ConsentLabels.DataProcessing, api.Published!.Type);
        cut.WaitForAssertion(() => Assert.Contains("Versión 1 vigente", cut.Markup));
    }

    // --- Historial ---

    [Theory]
    [InlineData("accepted:1:front_desk", "Aceptada (versión 1, Recepción (firmada en papel))")]
    [InlineData("accepted:0:app", "Aceptada (texto base, App)")]
    [InlineData("revoked:1", "Revocada (versión 1)")]
    [InlineData("none", "Sin aceptar")]
    public void History_values_of_consent_fields_read_in_spanish(string raw, string expected)
        => Assert.Equal(expected, MemberLabels.ChangeValue("consent_data_processing", raw));
}
