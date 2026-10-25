using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Communications;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>
/// Publicar un anuncio pide confirmación cuando llega lejos o no se puede deshacer: por email, importante
/// (push que se salta el horario sin avisos) o para todos. Antes solo el email la pedía.
/// </summary>
public class AnnouncementPublishConfirmUiTests : TestContext
{
    private static AnnouncementAudienceDto Everyone() => new() { Statuses = [], PlanIds = [], ClassIds = [], Tags = [] };

    private static AnnouncementAudienceDto Segment() => new() { Statuses = [], PlanIds = [], ClassIds = [], Tags = ["vip"] };

    private (IRenderedComponent<MudDialogProvider> Provider, IDialogService Dialogs, IAnnouncementsClient Client, CommercialStatusUiTests.FakeClient Fake) Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = CommercialStatusUiTests.Fake<IAnnouncementsClient>();
        fake.Handlers["AudiencePreviewAsync"] = _ => new AudiencePreviewResponse { Count = 214, Members = [] };
        fake.Handlers["EmailAudienceAsync"] = _ => new EmailAudienceResponse { Recipients = 180 };
        var provider = RenderComponent<MudDialogProvider>();
        return (provider, Services.GetRequiredService<IDialogService>(), client, fake);
    }

    [Fact]
    public async Task A_segmented_normal_announcement_without_email_publishes_without_asking()
    {
        var (provider, dialogs, client, fake) = Setup();

        var ok = await provider.InvokeAsync(() => AnnouncementActions.ConfirmPublishAsync(
            dialogs, client, sendEmail: false, important: false, category: 0, Segment()));

        Assert.True(ok);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task An_important_push_asks_with_the_reach_and_says_it_skips_quiet_hours()
    {
        var (provider, dialogs, client, _) = Setup();

        var pending = provider.InvokeAsync(() => AnnouncementActions.ConfirmPublishAsync(
            dialogs, client, sendEmail: false, important: true, category: 0, Segment()));

        provider.WaitForAssertion(() =>
        {
            Assert.Contains("Publicar anuncio importante", provider.Markup);
            Assert.Contains("214 miembros", provider.Markup);
            Assert.Contains("horario sin avisos", provider.Markup);
        });

        // "Volver" no publica.
        provider.FindAll("button").First(b => b.TextContent.Contains("Volver")).Click();
        Assert.False(await pending);
    }

    [Fact]
    public async Task Publishing_to_everyone_asks_even_without_email_and_names_the_count_on_the_button()
    {
        var (provider, dialogs, client, _) = Setup();

        var pending = provider.InvokeAsync(() => AnnouncementActions.ConfirmPublishAsync(
            dialogs, client, sendEmail: false, important: false, category: 0, Everyone()));

        provider.WaitForAssertion(() => Assert.Contains("Publicar para todos", provider.Markup));
        provider.FindAll("button").First(b => b.TextContent.Contains("Publicar a 214 miembros")).Click();
        Assert.True(await pending);
    }

    [Fact]
    public async Task With_email_it_also_says_how_many_get_the_mail_and_that_it_cannot_be_recalled()
    {
        var (provider, dialogs, client, _) = Setup();

        _ = provider.InvokeAsync(() => AnnouncementActions.ConfirmPublishAsync(
            dialogs, client, sendEmail: true, important: false, category: 0, Segment()));

        provider.WaitForAssertion(() =>
        {
            Assert.Contains("Publicar y enviar por email", provider.Markup);
            Assert.Contains("por email a 180 miembros", provider.Markup);
            Assert.Contains("no se pueden retirar", provider.Markup);
        });
    }
}
