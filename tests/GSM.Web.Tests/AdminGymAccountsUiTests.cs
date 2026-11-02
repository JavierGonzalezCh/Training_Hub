using Bunit;
using GSM.Client.Generated;
using GSM.UI.Shared.Components;
using GSM.Web.Pages.Admin.Gyms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>PW1: la sección Cuentas de la ficha del gym y el diálogo "Cambiar clave" del SuperAdmin.</summary>
public class AdminGymAccountsUiTests : TestContext
{
    private static readonly Guid GymId = Guid.NewGuid();

    private sealed class FakeAccountsClient : IAdminGymAccountsClient
    {
        public List<GymAccountResponse> Accounts { get; } = [];
        public List<string?> Searches { get; } = [];
        public List<(Guid UserId, string Password)> Saved { get; } = [];

        public Task<PagedResultOfGymAccountResponse> AccountsAsync(
            Guid gymId, string? search = null, int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
        {
            Searches.Add(search);
            var items = Accounts
                .Where(a => search is null || a.FullName.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(new PagedResultOfGymAccountResponse
            {
                Items = items, Page = page ?? 1, PageSize = pageSize ?? 20, Total = items.Count,
            });
        }

        public Task PasswordPUTAsync(Guid gymId, Guid userId, AdminSetPasswordRequest body, CancellationToken cancellationToken = default)
        {
            Saved.Add((userId, body.NewPassword));
            return Task.CompletedTask;
        }
    }

    private sealed class NoClipboard : GSM.UI.Shared.IClipboard
    {
        public Task<bool> CopyAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private (FakeAccountsClient Api, IRenderedComponent<MudDialogProvider> Dialogs) Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddScoped<ViewportState>();
        var api = new FakeAccountsClient();
        api.Accounts.Add(Account("Carlos Owner", "owner"));
        api.Accounts.Add(Account("Laura Miembro", "member", hasPassword: false));
        Services.AddSingleton<IAdminGymAccountsClient>(api);
        Services.AddSingleton<GSM.UI.Shared.IClipboard>(new NoClipboard());
        Services.GetRequiredService<ViewportState>().Pin(false);

        RenderComponent<MudPopoverProvider>();
        return (api, RenderComponent<MudDialogProvider>());
    }

    private static GymAccountResponse Account(string name, string role, bool hasPassword = true) => new()
    {
        UserId = Guid.NewGuid(), FullName = name, Email = $"{role}@t.l", Role = role, IsActive = true, HasPassword = hasPassword,
    };

    [Fact]
    public void Lists_every_account_with_its_role_and_whether_it_has_a_password()
    {
        Setup();

        var cut = RenderComponent<GymAccountsCard>(p => p.Add(c => c.GymId, GymId));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='admin-set-password']").Count));
        Assert.Contains("Miembro", cut.Markup);
        Assert.Contains("SIN CLAVE", cut.Markup);
        Assert.Contains("CON CLAVE", cut.Markup);
    }

    [Fact]
    public async Task Generating_a_password_and_saving_it_sends_it_for_that_person()
    {
        var (api, dialogs) = Setup();
        var cut = RenderComponent<GymAccountsCard>(p => p.Add(c => c.GymId, GymId));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='admin-set-password']").Count));

        await cut.InvokeAsync(() => cut.FindAll("[data-testid='admin-set-password']")[1].Click());
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='admin-generate-password']"));
        await dialogs.InvokeAsync(() => dialogs.Find("[data-testid='admin-generate-password']").Click());
        await dialogs.InvokeAsync(() => dialogs.Find("[data-testid='admin-save-password']").Click());

        dialogs.WaitForAssertion(() => Assert.Single(api.Saved));
        var (userId, password) = api.Saved[0];
        Assert.Equal(api.Accounts[1].UserId, userId);
        Assert.Equal(14, password.Length);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
    }

    [Fact]
    public async Task A_short_password_is_not_sent()
    {
        var (api, dialogs) = Setup();
        var cut = RenderComponent<GymAccountsCard>(p => p.Add(c => c.GymId, GymId));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='admin-set-password']").Count));

        await cut.InvokeAsync(() => cut.FindAll("[data-testid='admin-set-password']")[0].Click());
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='admin-save-password']"));
        dialogs.Find("input[data-testid='admin-new-password'], [data-testid='admin-new-password'] input").Input("corta");
        await dialogs.InvokeAsync(() => dialogs.Find("[data-testid='admin-save-password']").Click());

        dialogs.WaitForAssertion(() => Assert.Contains("al menos 10 caracteres", dialogs.Markup));
        Assert.Empty(api.Saved);
    }
}
