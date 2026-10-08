using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.UI.Shared.Auth;
using GSM.Web.Pages.Admin.Gyms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>S6: el diálogo para impersonar elige modo y motivo; la auditoría muestra sesiones y acciones.</summary>
public class ImpersonationUiTests : TestContext
{
    private sealed class FakeImpersonationClient : IAdminImpersonationClient
    {
        public OpenImpersonationRequest? Opened { get; private set; }

        public List<ImpersonationItem> Items { get; } = [];

        public List<ImpersonationActionItem> Actions { get; } = [];

        public Task<ImpersonationTokenResponse> ImpersonateAsync(
            Guid gymId, OpenImpersonationRequest body, CancellationToken cancellationToken = default)
        {
            Opened = body;
            return Task.FromResult(new ImpersonationTokenResponse
            {
                Token = "t",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
                SessionId = Guid.NewGuid(),
                GymId = gymId,
                Mode = body.Mode ?? "read",
            });
        }

        public Task EndAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ICollection<ImpersonationItem>> ImpersonationsAsync(
            Guid? gymId = null, int? limit = null, CancellationToken cancellationToken = default)
            => Task.FromResult<ICollection<ImpersonationItem>>(Items);

        public Task<ICollection<ImpersonationActionItem>> ActionsAsync(
            Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ICollection<ImpersonationActionItem>>(Actions);
    }

    private FakeImpersonationClient Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var client = new FakeImpersonationClient();
        Services.AddSingleton<IAdminImpersonationClient>(client);
        Services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        Services.AddSingleton<GymContext>();
        Services.AddSingleton<ImpersonationService>();
        this.AddTestAuthorization();
        return client;
    }

    [Fact]
    public async Task Dialog_asks_for_mode_and_reason_and_opens_in_write_mode()
    {
        var client = Setup();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<ImpersonateDialog>("Entrar como", new DialogParameters<ImpersonateDialog>
        {
            { x => x.GymId, Guid.NewGuid() },
            { x => x.GymName, "Gym Uno" },
        }));

        Assert.Contains("Solo lectura", provider.Markup);
        Assert.Contains("Escritura", provider.Markup);
        Assert.DoesNotContain("firmado", provider.Markup);   // el aviso aparece solo al elegir escritura

        var write = provider.FindAll("input[type=radio]")[1];
        write.Click();
        Assert.Contains("GSM", provider.Markup);

        provider.FindAll("textarea").First().Change("Corregir un cobro");
        provider.FindAll("button").Single(b => b.TextContent.Trim() == "Entrar").Click();

        provider.WaitForAssertion(() => Assert.NotNull(client.Opened));
        Assert.Equal("write", client.Opened!.Mode);
        Assert.Equal("Corregir un cobro", client.Opened.Reason);
    }

    [Fact]
    public async Task Dialog_defaults_to_read_mode_and_requires_a_reason()
    {
        var client = Setup();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<ImpersonateDialog>("Entrar como", new DialogParameters<ImpersonateDialog>
        {
            { x => x.GymId, Guid.NewGuid() },
            { x => x.GymName, "Gym Uno" },
        }));

        provider.FindAll("button").Single(b => b.TextContent.Trim() == "Entrar").Click();

        Assert.Contains("El motivo es obligatorio", provider.Markup);
        Assert.Null(client.Opened);
    }

    [Fact]
    public void Audit_page_lists_sessions_with_mode_and_action_count_and_expands_their_actions()
    {
        var client = Setup();
        var sessionId = Guid.NewGuid();
        client.Items.Add(new ImpersonationItem
        {
            Id = sessionId,
            GymId = Guid.NewGuid(),
            GymName = "Gym Uno",
            Reason = "Corregir un cobro",
            Mode = "write",
            ActionCount = 2,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        client.Actions.Add(new ImpersonationActionItem
        {
            Id = Guid.NewGuid(),
            Method = "POST",
            Endpoint = "api/payments",
            Summary = "Payments.Record",
            StatusCode = 201,
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-9),
        });

        var page = RenderComponent<GSM.Web.Pages.Admin.Impersonations.Index>();

        page.WaitForAssertion(() => Assert.Contains("Gym Uno", page.Markup));
        Assert.Contains("Escritura", page.Markup);
        Assert.Contains("Corregir un cobro", page.Markup);
        Assert.DoesNotContain("Payments.Record", page.Markup);

        page.FindAll("button").Single(b => b.TextContent.Trim() == "Ver acciones").Click();

        page.WaitForAssertion(() => Assert.Contains("Payments.Record", page.Markup));
        Assert.Contains("api/payments", page.Markup);
    }
}
