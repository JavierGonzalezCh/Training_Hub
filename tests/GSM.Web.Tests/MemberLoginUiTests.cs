using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.Member.UI.Auth;
using GSM.Member.UI.Pages;
using GSM.UI.Shared.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>I5 — pantalla de entrada del miembro (<c>/app/entrar</c>): marca del gym, contraseña y código por correo.</summary>
public class MemberLoginUiTests : TestContext
{
    /// <summary>Cliente de mentira: recuerda las llamadas y responde lo configurado (o lanza el error configurado).</summary>
    public class AuthStub : DispatchProxy
    {
        public List<RequestLoginCodeRequest> CodeRequests { get; } = [];
        public List<VerifyLoginCodeRequest> CodeVerifications { get; } = [];
        public List<LoginRequest> Logins { get; } = [];
        public bool VerifyFails { get; set; }
        public bool RequestThrottled { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result = null;
            switch (targetMethod!.Name)
            {
                case "RequestLoginCodeAsync":
                    CodeRequests.Add((RequestLoginCodeRequest)args![0]!);
                    if (RequestThrottled)
                    {
                        throw new GsmApiException(
                            "409", 409,
                            "{\"status\":409,\"detail\":\"Pediste demasiados códigos. Espera unos minutos e inténtalo de nuevo.\"}",
                            new Dictionary<string, IEnumerable<string>>(), null);
                    }

                    break;
                case "VerifyLoginCodeAsync":
                    CodeVerifications.Add((VerifyLoginCodeRequest)args![0]!);
                    if (VerifyFails)
                    {
                        throw new GsmApiException(
                            "401", 401,
                            "{\"status\":401,\"detail\":\"El código no es válido o venció. Pide uno nuevo.\"}",
                            new Dictionary<string, IEnumerable<string>>(), null);
                    }

                    result = Session();
                    break;
                case "LoginAsync":
                    Logins.Add((LoginRequest)args![0]!);
                    result = Session();
                    break;
                case "MeAsync":
                    result = new MeResponse
                    {
                        UserId = Guid.NewGuid(), FullName = "Ana", Email = "ana@t.l", Role = "member",
                        Gym = new MeGym { Name = "Iron Demo", PrimaryColor = "#B50016", SecondaryColor = "#0AAFCA" },
                    };
                    break;
            }

            return Wrap(targetMethod, result);
        }

        private static LoginResponse Session() => new()
        {
            RequiresGymSelection = false,
            Tokens = new AuthTokensResponse
            {
                AccessToken = "access-1", RefreshToken = "refresh-1", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
            },
        };
    }

    public class GymsStub : DispatchProxy
    {
        public string? RequestedSlug { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            RequestedSlug = (string)args![0]!;
            return Wrap(targetMethod!, new PublicGymBrandingResponse
            {
                Name = "Iron Demo", LogoUrl = null, PrimaryColor = "#B50016", SecondaryColor = "#0AAFCA",
            });
        }
    }

    private static object? Wrap(MethodInfo method, object? result)
    {
        if (method.ReturnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        var inner = method.ReturnType.GetGenericArguments()[0];
        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
    }

    private (AuthStub Auth, GymsStub Gyms, InMemoryTokenStore Tokens) Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var auth = DispatchProxy.Create<IAuthClient, AuthStub>();
        var gyms = DispatchProxy.Create<IPublicGymsClient, GymsStub>();
        var tokens = new InMemoryTokenStore();
        Services.AddSingleton(auth);
        Services.AddSingleton(gyms);
        Services.AddSingleton<ITokenStore>(tokens);
        Services.AddSingleton<GsmAuthenticationStateProvider>();
        Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<GsmAuthenticationStateProvider>());
        Services.AddSingleton<GymContext>();
        Services.AddSingleton<SessionService>();
        Services.AddSingleton<MemberLoginBranding>();
        return ((AuthStub)(object)auth, (GymsStub)(object)gyms, tokens);
    }

    [Fact]
    public void The_gym_slug_in_the_link_puts_the_gym_brand_on_the_screen()
    {
        var (_, gyms, _) = Setup();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/app/entrar?gym=iron-demo");

        var cut = RenderComponent<MemberLogin>();

        Assert.Equal("iron-demo", gyms.RequestedSlug);
        Assert.Contains("Iron Demo", cut.Find("[data-testid='gym-brand']").TextContent);
    }

    [Fact]
    public void Without_a_known_gym_the_screen_shows_the_gsm_brand()
    {
        Setup();

        var cut = RenderComponent<MemberLogin>();

        Assert.Single(cut.FindAll("[data-testid='gsm-brand']"));
        Assert.Empty(cut.FindAll("[data-testid='gym-brand']"));
    }

    [Fact]
    public void Asking_for_a_code_sends_the_email_and_shows_the_code_field()
    {
        var (auth, _, _) = Setup();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/app/entrar?gym=iron-demo");
        var cut = RenderComponent<MemberLogin>();

        cut.Find("[data-testid='use-code']").Click();
        cut.FindAll("input")[0].Input("ana@test.local");
        cut.Find("[data-testid='send-code']").Click();

        var request = Assert.Single(auth.CodeRequests);
        Assert.Equal("ana@test.local", request.Email);
        Assert.Equal("iron-demo", request.GymSlug);
        Assert.Equal(2, cut.FindAll("input").Count);
        Assert.Contains("Reenviar el código en", cut.Find("[data-testid='resend-code']").TextContent);
    }

    [Fact]
    public void Asking_for_too_many_codes_shows_the_server_message()
    {
        var (auth, _, _) = Setup();
        auth.RequestThrottled = true;
        var cut = RenderComponent<MemberLogin>();

        cut.Find("[data-testid='use-code']").Click();
        cut.FindAll("input")[0].Input("ana@test.local");
        cut.Find("[data-testid='send-code']").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            "Pediste demasiados códigos. Espera unos minutos e inténtalo de nuevo.",
            cut.Find("[data-testid='login-error']").TextContent));
    }

    [Fact]
    public void A_correct_code_starts_the_session_and_goes_to_the_member_area()
    {
        var (auth, _, tokens) = Setup();
        var cut = RenderComponent<MemberLogin>();
        cut.Find("[data-testid='use-code']").Click();
        cut.FindAll("input")[0].Input("ana@test.local");
        cut.Find("[data-testid='send-code']").Click();

        cut.FindAll("input")[1].Input("123456");
        cut.Find("[data-testid='verify-code']").Click();

        cut.WaitForAssertion(() => Assert.Single(auth.CodeVerifications));
        Assert.Equal("123456", auth.CodeVerifications[0].Code);
        cut.WaitForAssertion(() => Assert.NotNull(tokens.GetAsync().AsTask().Result));
        Assert.EndsWith("/app", Services.GetRequiredService<NavigationManager>().Uri.TrimEnd('/'));
    }

    [Fact]
    public void A_wrong_code_shows_the_server_message_and_keeps_the_screen()
    {
        var (auth, _, tokens) = Setup();
        auth.VerifyFails = true;
        var cut = RenderComponent<MemberLogin>();
        cut.Find("[data-testid='use-code']").Click();
        cut.FindAll("input")[0].Input("ana@test.local");
        cut.Find("[data-testid='send-code']").Click();

        cut.FindAll("input")[1].Input("000000");
        cut.Find("[data-testid='verify-code']").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='login-error']")));
        Assert.Null(tokens.GetAsync().AsTask().Result);
        Assert.Equal(2, cut.FindAll("input").Count);
    }

    [Fact]
    public void A_short_code_is_rejected_without_calling_the_server()
    {
        var (auth, _, _) = Setup();
        var cut = RenderComponent<MemberLogin>();
        cut.Find("[data-testid='use-code']").Click();
        cut.FindAll("input")[0].Input("ana@test.local");
        cut.Find("[data-testid='send-code']").Click();

        cut.FindAll("input")[1].Input("123");
        cut.Find("[data-testid='verify-code']").Click();

        Assert.Empty(auth.CodeVerifications);
        Assert.Contains("6 dígitos", cut.Find("[data-testid='login-error']").TextContent);
    }

    [Fact]
    public void Password_login_still_works_from_the_same_screen()
    {
        var (auth, _, tokens) = Setup();
        var cut = RenderComponent<MemberLogin>();

        cut.FindAll("input")[0].Input("ana@test.local");
        cut.FindAll("input")[1].Input("MiClave_12345");
        cut.Find("[data-testid='login-submit']").Click();

        cut.WaitForAssertion(() => Assert.Single(auth.Logins));
        Assert.Equal("MiClave_12345", auth.Logins[0].Password);
        cut.WaitForAssertion(() => Assert.NotNull(tokens.GetAsync().AsTask().Result));
    }
}
