using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>AP4: modo staff / modo miembro para quien tiene los dos accesos en un mismo gym.</summary>
public class ModeSwitchTests : TestContext
{
    private static readonly Guid GymA = Guid.NewGuid();
    private static readonly Guid GymB = Guid.NewGuid();

    private static GymAccessOption Access(Guid gym, string role, bool current = false) => new()
    {
        AccessId = Guid.NewGuid(),
        GymId = gym,
        GymName = gym == GymA ? "Gimnasio Demo" : "Otro Gym",
        Role = role,
        IsCurrent = current,
    };

    private sealed class FakeAuthClient : IAuthClient
    {
        public SwitchGymRequest? Switched { get; private set; }
        public string RoleAfterSwitch { get; set; } = "member";

        public Task<AuthTokensResponse> SwitchGymAsync(SwitchGymRequest body, CancellationToken cancellationToken = default)
        {
            Switched = body;
            return Task.FromResult(new AuthTokensResponse { AccessToken = "a2", RefreshToken = "r2", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) });
        }

        public Task<MeResponse> MeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MeResponse { UserId = Guid.NewGuid(), FullName = "Ana", Email = "ana@t.l", Role = RoleAfterSwitch });

        public Task<LoginResponse> ActivateAsync(ActivateAccountRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoginResponse> LoginAsync(LoginRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RequestLoginCodeAsync(RequestLoginCodeRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoginResponse> VerifyLoginCodeAsync(VerifyLoginCodeRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> SelectGymAsync(SelectGymRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> PairAsync(PairKioskRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> Refresh2Async(KioskRefreshRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ICollection<GymAccessOption>> AccessesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> RefreshAsync(RefreshRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(LogoutRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> InitialPasswordAsync(SetInitialPasswordRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PasswordPOSTAsync(ChangePasswordRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ForgotPasswordAsync(ForgotPasswordRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(ResetPasswordRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RequestEmailChangeAsync(RequestEmailChangeRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EmailChangedResponse> ConfirmEmailChangeAsync(ConfirmEmailChangeRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    // ---- Qué acceso es "el otro modo" ----

    [Fact]
    public void The_other_mode_is_the_access_of_the_other_kind_in_the_same_gym()
    {
        var staff = Access(GymA, "staff", current: true);
        var member = Access(GymA, "member");

        Assert.Same(member, AccessModes.OtherMode([staff, member, Access(GymB, "member")]));
        Assert.Equal("Pasar a modo miembro", AccessModes.SwitchLabel(member.Role));

        var inMemberMode = new[] { Access(GymA, "owner"), Access(GymA, "member", current: true) };
        Assert.Equal("owner", AccessModes.OtherMode(inMemberMode)!.Role);
        Assert.Equal("Pasar a modo staff", AccessModes.SwitchLabel("owner"));
    }

    [Fact]
    public void There_is_no_other_mode_with_a_single_access_or_when_the_second_one_is_in_another_gym()
    {
        Assert.Null(AccessModes.OtherMode([Access(GymA, "staff", current: true)]));
        Assert.Null(AccessModes.OtherMode([Access(GymA, "staff", current: true), Access(GymB, "member")]));
        Assert.Null(AccessModes.OtherMode([Access(GymA, "staff", current: true), Access(GymB, "staff")]));
        Assert.Null(AccessModes.OtherMode([Access(GymA, "staff"), Access(GymA, "member")]));
        Assert.Null(AccessModes.OtherMode([]));
    }

    // ---- El control ----

    [Fact]
    public void The_control_is_not_shown_without_a_second_mode_in_the_gym()
    {
        Setup();
        var cut = RenderComponent<ModeSwitch>(p => p
            .Add(x => x.Accesses, new[] { Access(GymA, "staff", current: true), Access(GymB, "member") })
            .Add(x => x.Compact, true));

        Assert.Empty(cut.FindAll("[data-testid='mode-switch']"));
    }

    [Fact]
    public void In_staff_mode_the_button_offers_member_mode_and_switching_issues_the_other_access_and_goes_to_app()
    {
        var (auth, tokens) = Setup();
        var member = Access(GymA, "member");
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        var cut = RenderComponent<ModeSwitch>(p => p
            .Add(x => x.Accesses, new[] { Access(GymA, "owner", current: true), member })
            .Add(x => x.Compact, true));

        var button = cut.Find("[data-testid='mode-switch']");
        Assert.Contains("modo miembro", button.GetAttribute("aria-label"));

        button.Click();

        cut.WaitForAssertion(() => Assert.NotNull(auth.Switched));
        Assert.Equal(member.AccessId, auth.Switched!.AccessId);
        Assert.Equal("refresh-1", auth.Switched.RefreshToken);
        cut.WaitForAssertion(() => Assert.EndsWith("/app", nav.Uri));
        Assert.Equal("a2", tokens.GetAsync().AsTask().Result!.AccessToken);
    }

    [Fact]
    public void In_member_mode_it_offers_staff_mode_and_returns_to_the_panel()
    {
        var (auth, _) = Setup();
        auth.RoleAfterSwitch = "owner";
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        var cut = RenderComponent<ModeSwitch>(p => p
            .Add(x => x.Accesses, new[] { Access(GymA, "owner"), Access(GymA, "member", current: true) })
            .Add(x => x.Compact, true));

        Assert.Contains("modo staff", cut.Find("[data-testid='mode-switch']").GetAttribute("aria-label"));

        cut.Find("[data-testid='mode-switch']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(auth.Switched));
        cut.WaitForAssertion(() => Assert.True(new Uri(nav.Uri).AbsolutePath == "/"));
    }
}
