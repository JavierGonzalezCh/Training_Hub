using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>I6: el formulario de cambio de email (dos pasos: correo nuevo y los dos códigos, I6-1).</summary>
public class ChangeEmailUiTests : TestContext
{
    public class AuthStub : DispatchProxy
    {
        public List<RequestEmailChangeRequest> Requests { get; } = [];
        public List<ConfirmEmailChangeRequest> Confirmations { get; } = [];

        /// <summary>Si se fija, la siguiente llamada falla con este status y detalle.</summary>
        public (int Status, string Detail)? FailsWith { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (FailsWith is { } failure)
            {
                FailsWith = null;
                throw new GsmApiException(
                    failure.Status.ToString(), failure.Status,
                    $"{{\"status\":{failure.Status},\"detail\":\"{failure.Detail}\"}}",
                    new Dictionary<string, IEnumerable<string>>(), null);
            }

            switch (targetMethod!.Name)
            {
                case "RequestEmailChangeAsync":
                    Requests.Add((RequestEmailChangeRequest)args![0]!);
                    return Task.CompletedTask;
                case "MeAsync":
                    return Task.FromResult(new MeResponse());
                case "ConfirmEmailChangeAsync":
                    var confirm = (ConfirmEmailChangeRequest)args![0]!;
                    Confirmations.Add(confirm);
                    return Task.FromResult(new EmailChangedResponse { Email = Requests.Last().NewEmail });
                default:
                    throw new NotSupportedException(targetMethod.Name);
            }
        }
    }

    private AuthStub Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var auth = DispatchProxy.Create<IAuthClient, AuthStub>();
        var store = new InMemoryTokenStore();
        Services.AddSingleton(auth);
        Services.AddSingleton<ITokenStore>(store);
        Services.AddSingleton<GsmAuthenticationStateProvider>();
        Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<GsmAuthenticationStateProvider>());
        Services.AddSingleton<GymContext>();
        Services.AddSingleton<SessionService>();
        return (AuthStub)(object)auth;
    }

    [Fact]
    public void Asking_for_a_new_email_sends_the_code_and_shows_the_code_step()
    {
        var auth = Setup();
        var cut = RenderComponent<ChangeEmailForm>();

        cut.Find("input").Input("nuevo@correo.com");
        cut.Find("[data-testid='change-email-request']").Click();

        Assert.Equal("nuevo@correo.com", auth.Requests.Single().NewEmail);
        Assert.Contains("nuevo@correo.com", cut.Find("[data-testid='change-email-sent']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='change-email-error']"));
    }

    [Fact]
    public void An_invalid_email_is_rejected_before_calling_the_api()
    {
        var auth = Setup();
        var cut = RenderComponent<ChangeEmailForm>();

        cut.Find("input").Input("no-es-un-correo");
        cut.Find("[data-testid='change-email-request']").Click();

        Assert.Empty(auth.Requests);
        Assert.Contains("correo válido", cut.Find("[data-testid='change-email-error']").TextContent);
    }

    [Fact]
    public void An_email_already_in_use_shows_the_server_message_and_stays_on_the_first_step()
    {
        var auth = Setup();
        auth.FailsWith = (409, "Ese email ya está en uso por otra cuenta.");
        var cut = RenderComponent<ChangeEmailForm>();

        cut.Find("input").Input("ocupado@correo.com");
        cut.Find("[data-testid='change-email-request']").Click();

        Assert.Contains("ya está en uso", cut.Find("[data-testid='change-email-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='change-email-sent']"));
    }

    [Fact]
    public void Confirming_with_the_code_shows_the_new_email_as_done()
    {
        var auth = Setup();
        var cut = RenderComponent<ChangeEmailForm>();
        cut.Find("input").Input("nuevo@correo.com");
        cut.Find("[data-testid='change-email-request']").Click();

        // I6-1: el código del correo nuevo y el del actual.
        cut.FindAll("input")[0].Input("123456");
        cut.FindAll("input")[1].Input("654321");
        cut.Find("[data-testid='change-email-confirm']").Click();

        Assert.Equal("123456", auth.Confirmations.Single().Code);
        Assert.Equal("654321", auth.Confirmations.Single().CurrentEmailCode);
        Assert.Contains("nuevo@correo.com", cut.Find("[data-testid='change-email-done']").TextContent);
        // Vuelve al primer paso, listo para otro cambio.
        Assert.Empty(cut.FindAll("[data-testid='change-email-confirm']"));
    }

    [Fact]
    public void Without_the_code_of_the_current_email_it_does_not_call_the_api()
    {
        var auth = Setup();
        var cut = RenderComponent<ChangeEmailForm>();
        cut.Find("input").Input("nuevo@correo.com");
        cut.Find("[data-testid='change-email-request']").Click();
        Assert.Contains("correo actual", cut.Find("[data-testid='change-email-sent']").TextContent);

        cut.FindAll("input")[0].Input("123456");
        cut.Find("[data-testid='change-email-confirm']").Click();

        Assert.Empty(auth.Confirmations);
        Assert.Contains("6 dígitos", cut.Find("[data-testid='change-email-error']").TextContent);
    }

    [Fact]
    public void A_malformed_code_is_rejected_locally_and_a_wrong_one_shows_the_server_message()
    {
        var auth = Setup();
        var cut = RenderComponent<ChangeEmailForm>();
        cut.Find("input").Input("nuevo@correo.com");
        cut.Find("[data-testid='change-email-request']").Click();

        cut.FindAll("input")[0].Input("12ab");
        cut.FindAll("input")[1].Input("123456");
        cut.Find("[data-testid='change-email-confirm']").Click();
        Assert.Empty(auth.Confirmations);
        Assert.Contains("6 dígitos", cut.Find("[data-testid='change-email-error']").TextContent);

        auth.FailsWith = (422, "El código no es válido o venció. Pide uno nuevo.");
        cut.FindAll("input")[0].Input("654321");
        cut.Find("[data-testid='change-email-confirm']").Click();
        Assert.Contains("no es válido", cut.Find("[data-testid='change-email-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='change-email-done']"));
    }
}
