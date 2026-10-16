using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>M6 — pestaña "Privacidad" de la configuración: plazo de anonimización del gym.</summary>
public class MemberAnonymizationUiTests : TestContext
{
    private sealed class FakeMemberSettings(int months) : IMemberSettingsClient
    {
        public UpdateMemberSettingsRequest? Updated { get; private set; }

        public Task<MemberSettingsResponse> GetMemberSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MemberSettingsResponse { AnonymizationMonths = months, NoticeDays = 7 });

        public Task<MemberSettingsResponse> UpdateMemberSettingsAsync(
            UpdateMemberSettingsRequest body, CancellationToken cancellationToken = default)
        {
            Updated = body;
            return Task.FromResult(new MemberSettingsResponse { AnonymizationMonths = body.AnonymizationMonths, NoticeDays = 7 });
        }
    }

    private void UseMud()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void The_tab_shows_the_current_period_and_explains_what_is_kept()
    {
        UseMud();
        Services.AddSingleton<IMemberSettingsClient>(new FakeMemberSettings(18));

        var cut = RenderComponent<AnonymizationTab>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("18", cut.Find("[data-testid=anonymization-months] input, input[data-testid=anonymization-months]").GetAttribute("value"));
            Assert.Contains("irreversible", cut.Markup);
            Assert.Contains("pagos, asistencias y membresías", cut.Markup);
        });
    }

    [Fact]
    public void Saving_sends_the_period_to_the_api()
    {
        UseMud();
        var api = new FakeMemberSettings(24);
        Services.AddSingleton<IMemberSettingsClient>(api);

        var cut = RenderComponent<AnonymizationTab>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=anonymization-save]")));

        cut.Find("[data-testid=anonymization-save]").Click();

        cut.WaitForAssertion(() => Assert.Equal(24, api.Updated!.AnonymizationMonths));
    }
}
