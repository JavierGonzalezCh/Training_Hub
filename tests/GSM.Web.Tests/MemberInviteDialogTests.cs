using Bunit;
using GSM.Panel.UI.Pages.Members;
using GSM.UI.Shared;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

public class MemberInviteDialogTests : TestContext
{
    private sealed class FakeClipboard(bool succeeds = true) : IClipboard
    {
        public List<string> Copied { get; } = [];

        public Task<bool> CopyAsync(string text, CancellationToken cancellationToken = default)
        {
            Copied.Add(text);
            return Task.FromResult(succeeds);
        }
    }

    private async Task<(FakeClipboard Clipboard, IRenderedComponent<MudDialogProvider> Provider)> OpenAsync(bool succeeds = true)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var clipboard = new FakeClipboard(succeeds);
        Services.AddSingleton<IClipboard>(clipboard);

        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<MemberInviteDialog>("Invitación", new DialogParameters<MemberInviteDialog>
        {
            { x => x.Code, "ABC-123" },
            { x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(7) },
        }));
        return (clipboard, provider);
    }

    [Fact]
    public async Task Copy_button_sends_the_invite_code_to_the_clipboard()
    {
        var (clipboard, provider) = await OpenAsync();

        provider.Find("button[title='Copiar']").Click();

        Assert.Equal(["ABC-123"], clipboard.Copied);
    }

    [Fact]
    public async Task Copy_failure_does_not_throw()
    {
        var (clipboard, provider) = await OpenAsync(succeeds: false);

        provider.Find("button[title='Copiar']").Click();

        Assert.Single(clipboard.Copied);
    }
}
