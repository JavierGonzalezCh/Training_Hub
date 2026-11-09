using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Settings;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// A8 — al dejar al gym sin registro de entrada en la puerta con logros o progreso encendidos, el panel advierte al
/// dueño que el miembro podría marcar su entrenamiento desde la casa.
/// </summary>
public class RoutineEntryRiskUiTests : TestContext
{
    [Theory]
    // antes: QR, documento, logros, progreso -> después
    [InlineData(true, false, true, true, false, false, true, true, true)]     // apaga el QR con logros
    [InlineData(false, true, true, false, false, false, true, false, true)]   // apaga el documento con logros
    [InlineData(false, false, false, false, false, false, true, false, true)] // enciende logros sin registro
    [InlineData(false, false, false, false, false, false, false, true, true)] // enciende progreso sin registro
    [InlineData(true, false, false, false, false, false, false, false, false)] // sin logros ni progreso: nada que fingir
    [InlineData(false, false, true, true, false, false, true, true, false)]   // ya estaba así: no se repite
    [InlineData(true, false, true, true, true, false, true, true, false)]     // nada cambia
    public void Warns_only_when_the_change_leads_to_no_door_registration_with_progress_on(
        bool qrBefore, bool docBefore, bool achBefore, bool progBefore,
        bool qrAfter, bool docAfter, bool achAfter, bool progAfter, bool warn)
        => Assert.Equal(warn, RoutineEntryRisk.ShouldWarn(
            new RoutineEntryRisk.State(qrBefore, docBefore, achBefore, progBefore),
            new RoutineEntryRisk.State(qrAfter, docAfter, achAfter, progAfter)));

    [Fact]
    public void Remote_training_removes_the_risk_because_marking_from_home_is_expected()
    {
        var local = new RoutineEntryRisk.State(false, false, true, true);
        var remote = local with { RemoteTraining = true };

        Assert.True(local.AtRisk);
        Assert.False(remote.AtRisk);
        // Encenderlo no avisa; apagarlo estando sin puerta sí.
        Assert.False(RoutineEntryRisk.ShouldWarn(local, remote));
        Assert.True(RoutineEntryRisk.ShouldWarn(remote, local));
    }

    private (IRenderedComponent<CheckinTab> Tab, IRenderedComponent<MudDialogProvider> Dialogs, FakeClient Fake) RenderCheckinTab(
        out PaymentPermissionsUiTests.FakeClient gymFake, bool remote = false, bool byDocument = true, bool checkinQr = true)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var attendance = System.Reflection.DispatchProxy.Create<IAttendanceClient, FakeClient>();
        var fake = (FakeClient)(object)attendance;
        fake.Handlers["SettingsGETAsync"] = _ => new CheckinSettingsResponse
        {
            Mode = "Rotating", ByDocument = byDocument, AssumedStayMinutes = 120, RemoteTraining = remote,
        };
        fake.Handlers["SettingsPUTAsync"] = args =>
        {
            var put = (UpdateCheckinSettingsRequest)args[0]!;
            return new CheckinSettingsResponse
            {
                Mode = put.Mode, ByDocument = put.ByDocument ?? false, AssumedStayMinutes = 120,
                RemoteTraining = put.RemoteTraining,
            };
        };
        Services.AddSingleton(attendance);

        var gym = new GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { CheckinQr = checkinQr, ShowGamification = true } }, false);
        Services.AddSingleton(gym);
        gymFake = CheckinTabServices.AddFor(this, checkinQr);

        var dialogs = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        var tab = RenderComponent<CheckinTab>();
        tab.WaitForAssertion(() => Assert.Contains("Registro de entrada", tab.Markup));
        return (tab, dialogs, fake);
    }

    private static void TurnDocumentOffAndSave(IRenderedComponent<CheckinTab> tab)
    {
        tab.Find("[data-testid='checkin-by-document'] input, input[data-testid='checkin-by-document']").Change(false);
        tab.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();
    }

    [Fact]
    public void The_remote_training_option_is_sent_when_saving()
    {
        var (tab, _, fake) = RenderCheckinTab(out _, remote: false);

        tab.Find("[data-testid='checkin-remote-training']").Change(true);
        tab.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();

        tab.WaitForAssertion(() =>
        {
            var put = Assert.IsType<UpdateCheckinSettingsRequest>(
                Assert.Single(fake.Calls, c => c.Method == "SettingsPUTAsync").Args[0]);
            Assert.True(put.RemoteTraining);
        });
    }

    [Fact]
    public void Choosing_no_door_registration_turns_the_gym_qr_and_the_document_entry_off()
    {
        var (tab, _, fake) = RenderCheckinTab(out var gymFake, byDocument: true, checkinQr: true);

        tab.Find("input[name='checkin-mode'][value='NoDoor']").Change("NoDoor");
        Assert.Empty(tab.FindAll("[data-testid='checkin-by-document']"));
        tab.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();

        tab.WaitForAssertion(() =>
        {
            var flags = Assert.IsType<UpdateFeatureFlagsRequest>(Assert.Single(gymFake.Calls, c => c.Method == "FeatureFlagsPUTAsync").Args[0]);
            Assert.False(flags.CheckinQr);
            var put = Assert.IsType<UpdateCheckinSettingsRequest>(Assert.Single(fake.Calls, c => c.Method == "SettingsPUTAsync").Args[0]);
            Assert.False(put.ByDocument);
        });
    }

    [Fact]
    public void A_gym_without_the_qr_opens_with_no_door_registration_selected()
    {
        var (tab, _, _) = RenderCheckinTab(out _, byDocument: false, checkinQr: false);

        Assert.Contains("is-selected", tab.Find("input[name='checkin-mode'][value='NoDoor']").ParentElement!.ClassName);
        Assert.Empty(tab.FindAll("[data-testid='checkin-by-document']"));
        Assert.Contains("El día cuenta cuando el miembro registra su primer ejercicio.", tab.Markup);
    }

    [Fact]
    public void Picking_the_qr_back_turns_it_on_without_touching_the_document_entry_value()
    {
        var (tab, _, _) = RenderCheckinTab(out var gymFake, byDocument: false, checkinQr: false);

        tab.Find("input[name='checkin-mode'][value='Rotating']").Change("Rotating");
        tab.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();

        tab.WaitForAssertion(() =>
            Assert.True(Assert.IsType<UpdateFeatureFlagsRequest>(
                Assert.Single(gymFake.Calls, c => c.Method == "FeatureFlagsPUTAsync").Args[0]).CheckinQr));
    }
}
