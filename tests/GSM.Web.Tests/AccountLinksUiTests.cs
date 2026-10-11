using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using GSM.Web.Pages;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>I1: la pantalla de cambiar contraseña y la de activar con el formulario precargado.</summary>
public class AccountLinksUiTests : TestContext
{
    private sealed class FakeAuthClient : IAuthClient
    {
        public ChangePasswordRequest? Changed { get; private set; }
        public ActivateAccountRequest? Activated { get; private set; }
        public int MeCalls { get; private set; }

        /// <summary>Si se fija, el siguiente cambio de contraseña falla con este 422.</summary>
        public string? ChangeFailsWith { get; set; }

        public Task PasswordAsync(ChangePasswordRequest body, CancellationToken cancellationToken = default)
        {
            if (ChangeFailsWith is not null)
            {
                throw new GsmApiException("422", 422, $"{{\"status\":422,\"detail\":\"{ChangeFailsWith}\"}}", new Dictionary<string, IEnumerable<string>>(), null);
            }

            Changed = body;
            return Task.CompletedTask;
        }

        public Task<MeResponse> MeAsync(CancellationToken cancellationToken = default)
        {
            MeCalls++;
            return Task.FromResult(new MeResponse { UserId = Guid.NewGuid(), FullName = "Ana", Email = "ana@t.l", Role = "staff" });
        }

        public Task<LoginResponse> ActivateAsync(ActivateAccountRequest body, CancellationToken cancellationToken = default)
        {
            Activated = body;
            return Task.FromResult(new LoginResponse
            {
                RequiresGymSelection = false,
                Tokens = new AuthTokensResponse { AccessToken = "a", RefreshToken = "r", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) },
            });
        }

        public Task<LoginResponse> LoginAsync(LoginRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> SelectGymAsync(SelectGymRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> SwitchGymAsync(SwitchGymRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ICollection<GymAccessOption>> AccessesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> RefreshAsync(RefreshRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(LogoutRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ForgotPasswordAsync(ForgotPasswordRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(ResetPasswordRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private (FakeAuthClient Auth, InMemoryTokenStore Tokens) Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var auth = new FakeAuthClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetAsync(new AuthTokens("access-1", "refresh-1", DateTimeOffset.UtcNow.AddHours(1))).AsTask().Wait();
        Services.AddSingleton<IAuthClient>(auth);
        Services.AddSingleton<ITokenStore>(tokens);
        Services.AddSingleton<GsmAuthenticationStateProvider>();
        Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<GsmAuthenticationStateProvider>());
        Services.AddSingleton<GymContext>();
        Services.AddSingleton<SessionService>();
        return (auth, tokens);
    }

    private static void Type(IRenderedComponent<ChangePasswordForm> cut, int index, string value)
        => cut.FindAll("input")[index].Input(value);

    // ---- Cambiar contraseña ----

    [Fact]
    public void Change_password_sends_the_current_and_new_passwords_with_the_current_session_refresh_token()
    {
        var (auth, _) = Setup();
        var cut = RenderComponent<ChangePasswordForm>();

        Type(cut, 0, "OldPass_12345");
        Type(cut, 1, "NewPass_67890");
        Type(cut, 2, "NewPass_67890");
        cut.Find("button").Click();

        Assert.Equal("OldPass_12345", auth.Changed!.CurrentPassword);
        Assert.Equal("NewPass_67890", auth.Changed.NewPassword);
        // El servidor conserva esa sesión y cierra las demás.
        Assert.Equal("refresh-1", auth.Changed.RefreshToken);
        // Antes de cambiar se fuerza una llamada autenticada, para que un refresh no deje un token revocado en el cuerpo.
        Assert.Equal(1, auth.MeCalls);
        Assert.Contains("otras sesiones", cut.Find("[data-testid='change-password-done']").TextContent);
    }

    [Fact]
    public void Change_password_validates_the_confirmation_and_length_before_calling_the_api()
    {
        var (auth, _) = Setup();
        var cut = RenderComponent<ChangePasswordForm>();

        Type(cut, 0, "OldPass_12345");
        Type(cut, 1, "NewPass_67890");
        Type(cut, 2, "Different_12345");
        cut.Find("button").Click();
        Assert.Contains("no coinciden", cut.Find("[data-testid='change-password-error']").TextContent);

        Type(cut, 1, "corta");
        Type(cut, 2, "corta");
        cut.Find("button").Click();
        Assert.Contains("al menos 10", cut.Find("[data-testid='change-password-error']").TextContent);

        Assert.Null(auth.Changed);
    }

    [Fact]
    public void A_wrong_current_password_shows_the_server_message_and_no_success()
    {
        var (auth, _) = Setup();
        auth.ChangeFailsWith = "La contraseña actual no es correcta.";
        var cut = RenderComponent<ChangePasswordForm>();

        Type(cut, 0, "Wrong_Pass_999");
        Type(cut, 1, "NewPass_67890");
        Type(cut, 2, "NewPass_67890");
        cut.Find("button").Click();

        Assert.Contains("actual no es correcta", cut.Find("[data-testid='change-password-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='change-password-done']"));
    }

    // ---- Activar por enlace ----

    private IRenderedComponent<Activate> RenderActivate(string? email, string? code)
    {
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        var query = code is null ? $"?email={Uri.EscapeDataString(email ?? "")}" : $"?email={Uri.EscapeDataString(email ?? "")}&code={Uri.EscapeDataString(code)}";
        nav.NavigateTo("/activar" + query);
        return RenderComponent<Activate>();
    }

    [Fact]
    public void The_activation_link_opens_a_form_with_the_email_filled_in_and_only_the_password_missing()
    {
        var (auth, _) = Setup();
        var cut = RenderActivate("ana@test.local", "CODE/with+chars==");

        var inputs = cut.FindAll("input");
        Assert.Equal(3, inputs.Count); // email (solo lectura), contraseña, repetir
        Assert.Equal("ana@test.local", inputs[0].GetAttribute("value"));
        Assert.True(inputs[0].HasAttribute("readonly"));

        inputs[1].Input("StaffPass_12345");
        inputs[2].Input("StaffPass_12345");
        cut.Find("button").Click();

        Assert.Equal("ana@test.local", auth.Activated!.Email);
        Assert.Equal("CODE/with+chars==", auth.Activated.Code);
        Assert.Equal("StaffPass_12345", auth.Activated.NewPassword);
    }

    [Fact]
    public void A_link_without_a_code_asks_the_person_to_log_in_instead_of_creating_a_password()
    {
        Setup();
        var cut = RenderActivate("ana@test.local", null);

        Assert.Contains("Ya tienes una cuenta", cut.Markup);
        Assert.Empty(cut.FindAll("input"));
        Assert.Contains("/login?email=ana%40test.local", cut.Find("a[href^='/login']").GetAttribute("href"));
    }

    [Fact]
    public void Mismatched_passwords_are_rejected_without_calling_the_api()
    {
        var (auth, _) = Setup();
        var cut = RenderActivate("ana@test.local", "CODE");

        var inputs = cut.FindAll("input");
        inputs[1].Input("StaffPass_12345");
        inputs[2].Input("Another_12345");
        cut.Find("button").Click();

        Assert.Contains("no coinciden", cut.Find("[data-testid='activate-error']").TextContent);
        Assert.Null(auth.Activated);
    }
}
