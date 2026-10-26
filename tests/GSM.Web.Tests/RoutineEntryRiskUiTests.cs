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
        bool remote = false, string trigger = "Start", bool byDocument = true)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var attendance = System.Reflection.DispatchProxy.Create<IAttendanceClient, FakeClient>();
        var fake = (FakeClient)(object)attendance;
        fake.Handlers["SettingsGETAsync"] = _ => new CheckinSettingsResponse
        {
            Mode = "Rotating", ByDocument = byDocument, AssumedStayMinutes = 120, RemoteTraining = remote, TrainingEntryTrigger = trigger,
        };
        fake.Handlers["SettingsPUTAsync"] = args =>
        {
            var put = (UpdateCheckinSettingsRequest)args[0]!;
            return new CheckinSettingsResponse
            {
                Mode = put.Mode, ByDocument = put.ByDocument ?? false, AssumedStayMinutes = 120,
                RemoteTraining = put.RemoteTraining, TrainingEntryTrigger = put.TrainingEntryTrigger,
            };
        };
        Services.AddSingleton(attendance);

        // QR apagado y logros encendidos: el documento es el único registro en la puerta.
        var gym = new GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { CheckinQr = false, ShowGamification = true } }, false);
        Services.AddSingleton(gym);

        var dialogs = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        var tab = RenderComponent<CheckinTab>();
        tab.WaitForAssertion(() => Assert.Contains("Tipo de QR en la entrada", tab.Markup));
        return (tab, dialogs, fake);
    }

    private static void TurnDocumentOffAndSave(IRenderedComponent<CheckinTab> tab)
    {
        tab.Find("[data-testid='checkin-by-document'] input, input[data-testid='checkin-by-document']").Change(false);
        tab.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();
    }

    [Fact]
    public void Turning_off_the_last_door_registration_asks_first_and_going_back_saves_nothing()
    {
        var (tab, dialogs, fake) = RenderCheckinTab();

        TurnDocumentOffAndSave(tab);

        dialogs.WaitForAssertion(() => Assert.Contains("desde su casa", dialogs.Markup));
        dialogs.FindAll("button").First(b => b.TextContent.Contains("Volver")).Click();

        Assert.DoesNotContain(fake.Calls, c => c.Method == "SettingsPUTAsync");
    }

    [Fact]
    public void Confirming_the_warning_saves_the_change()
    {
        var (tab, dialogs, fake) = RenderCheckinTab();

        TurnDocumentOffAndSave(tab);

        dialogs.WaitForAssertion(() => Assert.Contains(RoutineEntryRisk.Title, dialogs.Markup));
        dialogs.FindAll("button").First(b => b.TextContent.Contains("Guardar así")).Click();

        tab.WaitForAssertion(() =>
        {
            var put = Assert.Single(fake.Calls, c => c.Method == "SettingsPUTAsync");
            Assert.False(Assert.IsType<UpdateCheckinSettingsRequest>(put.Args[0]).ByDocument);
        });
    }

    [Fact]
    public void The_two_new_options_load_from_the_server_and_are_sent_when_saving()
    {
        var (tab, _, fake) = RenderCheckinTab(remote: false, trigger: "Start");

        tab.Find("[data-testid='checkin-remote-training']").Change(true);
        tab.Find("[data-testid='checkin-trigger'] input[value='FirstLog']").Change("FirstLog");
        tab.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();

        tab.WaitForAssertion(() =>
        {
            var put = Assert.IsType<UpdateCheckinSettingsRequest>(
                Assert.Single(fake.Calls, c => c.Method == "SettingsPUTAsync").Args[0]);
            Assert.True(put.RemoteTraining);
            Assert.Equal("FirstLog", put.TrainingEntryTrigger);
        });
    }

    [Fact]
    public void With_remote_training_on_turning_the_document_off_does_not_warn()
    {
        var (tab, dialogs, fake) = RenderCheckinTab(remote: true, trigger: "FirstLog");

        TurnDocumentOffAndSave(tab);

        tab.WaitForAssertion(() => Assert.Single(fake.Calls, c => c.Method == "SettingsPUTAsync"));
        Assert.DoesNotContain(RoutineEntryRisk.Title, dialogs.Markup);
    }
}
