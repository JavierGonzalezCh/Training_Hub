using Bunit;
using GSM.Client.Generated;
using GSM.Web.Pages.Admin.Gyms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>G1 + S1: el alta de gym ya no pide contraseña del owner y ofrece prueba con días o primer pago.</summary>
public class GymCreationDialogTests : TestContext
{
    private sealed class FakeGymsClient : IGymsClient
    {
        public CreateGymRequest? Created { get; private set; }

        public Task<CreateGymResponse> GymsPOSTAsync(CreateGymRequest body, CancellationToken cancellationToken = default)
        {
            Created = body;
            return Task.FromResult(new CreateGymResponse { GymId = Guid.NewGuid(), OwnerId = Guid.NewGuid(), OwnerInvitationSent = true });
        }
    }

    private async Task<(FakeGymsClient Client, IRenderedComponent<MudDialogProvider> Provider)> OpenAsync()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var client = new FakeGymsClient();
        Services.AddSingleton<IGymsClient>(client);

        RenderComponent<MudPopoverProvider>();   // el selector del método de pago lo necesita
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<GymFormDialog>("Nuevo gym"));
        return (client, provider);
    }

    [Fact]
    public async Task The_form_has_no_owner_password_field_and_explains_the_activation_link()
    {
        var (_, provider) = await OpenAsync();

        Assert.Empty(provider.FindAll("input[type='password']"));
        Assert.Contains("enlace", provider.Find("[data-testid='owner-invite-note']").TextContent);
    }

    [Fact]
    public async Task With_trial_on_by_default_it_asks_for_the_days_and_not_for_a_payment()
    {
        var (_, provider) = await OpenAsync();

        Assert.NotEmpty(provider.FindAll("[data-testid='trial-days']"));
        Assert.Empty(provider.FindAll("[data-testid='first-payment-amount']"));
    }

    [Fact]
    public async Task Turning_the_trial_off_asks_for_the_first_payment_instead_of_the_days()
    {
        var (_, provider) = await OpenAsync();

        provider.Find("input[data-testid='with-trial']").Change(false);

        Assert.Empty(provider.FindAll("[data-testid='trial-days']"));
        Assert.NotEmpty(provider.FindAll("[data-testid='first-payment-amount']"));
        Assert.Contains("primer pago", provider.Find("[data-testid='first-payment-note']").TextContent);
    }
}
