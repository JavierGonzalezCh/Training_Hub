using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.UI.Shared.Auth;
using GSM.Web.Pages;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>A3: el kiosco solo lo abre la cuenta de dispositivo; sin ella, emparejamiento.</summary>
public class KioskDeviceUiTests : TestContext
{
    private sealed class FakeAuthClient : IAuthClient
    {
        public PairKioskRequest? Paired { get; private set; }
        public string AccessToken { get; set; } = "";

        public Task<AuthTokensResponse> PairAsync(PairKioskRequest body, CancellationToken cancellationToken = default)
        {
            Paired = body;
            return Task.FromResult(new AuthTokensResponse
            {
                AccessToken = AccessToken, RefreshToken = "device-refresh", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
            });
        }

        public Task<AuthTokensResponse> Refresh2Async(KioskRefreshRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MeResponse> MeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PasswordAsync(ChangePasswordRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoginResponse> ActivateAsync(ActivateAccountRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoginResponse> LoginAsync(LoginRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RequestLoginCodeAsync(RequestLoginCodeRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoginResponse> VerifyLoginCodeAsync(VerifyLoginCodeRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> SelectGymAsync(SelectGymRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> SwitchGymAsync(SwitchGymRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ICollection<GymAccessOption>> AccessesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokensResponse> RefreshAsync(RefreshRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(LogoutRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ForgotPasswordAsync(ForgotPasswordRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(ResetPasswordRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RequestEmailChangeAsync(RequestEmailChangeRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EmailChangedResponse> ConfirmEmailChangeAsync(ConfirmEmailChangeRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeAttendanceClient : IAttendanceClient
    {
        /// <summary>Si se fija, el código del QR falla con este status.</summary>
        public int? FailWith { get; set; }

        public int Calls { get; private set; }

        /// <summary>A4: el gym tiene encendida la entrada con documento.</summary>
        public bool ByDocument { get; set; }
        public bool QrDisabled { get; set; }

        /// <summary>A4: status con el que falla la entrada con documento (409 rechazo, 429 bloqueo); nulo = entra.</summary>
        public int? DocumentFailWith { get; set; }

        public List<string> Documents { get; } = [];

        public Task<KioskDocumentEntryResponse> KioskDocumentEntryAsync(KioskDocumentEntryRequest body, CancellationToken cancellationToken = default)
        {
            Documents.Add(body.Document);
            if (DocumentFailWith is { } status)
            {
                throw new GsmApiException(status.ToString(), status, "{}", new Dictionary<string, IEnumerable<string>>(), null);
            }

            return Task.FromResult(new KioskDocumentEntryResponse { FirstName = "Ana", PhotoUrl = null, ExpiringSoon = true });
        }

        public PublicGymBrandingResponse? Branding { get; set; }

        public Task<PublicGymBrandingResponse> KioskBrandingAsync(CancellationToken cancellationToken = default)
            => Branding is null
                ? throw new GsmApiException("404", 404, "{}", new Dictionary<string, IEnumerable<string>>(), null)
                : Task.FromResult(Branding);

        public Task<KioskCodeResponse> KioskCodeAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailWith is { } status)
            {
                throw new GsmApiException(status.ToString(), status, "{}", new Dictionary<string, IEnumerable<string>>(), null);
            }

            return Task.FromResult(new KioskCodeResponse
            {
                Mode = "Rotating", Code = "abc123", ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(30), ByDocument = ByDocument, QrDisabled = QrDisabled,
            });
        }

        public Task<CheckInResponse> CheckinAsync(CheckInRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReceptionCardResponse> ReceptionCardAsync(Guid memberId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EntryRuleResponse> GetEntryRuleAsync(Guid memberId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EntryRuleResponse> SetEntryRuleAsync(Guid memberId, SetEntryRuleRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveEntryRuleAsync(Guid memberId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task VoidCheckInAsync(Guid id, VoidCheckInRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResultOfAttendanceListItem> AttendanceGETAsync(Guid? memberId = null, DateOnly? from = null, DateOnly? to = null, bool? authorizedOnly = null, bool? documentOnly = null, bool? includeVoided = null, bool? notCountedOnly = null, int? page = null, int? pageSize = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CheckinLocationPolicyResponse> GetCheckinLocationPolicyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ICollection<PeakHourBucket>> PeakHoursAsync(DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OccupancyResponse> GetOccupancyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CheckinSettingsResponse> SettingsGETAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CheckinSettingsResponse> SettingsPUTAsync(UpdateCheckinSettingsRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<KioskCodeResponse> RegenerateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResultOfCheckinAttemptItem> AttemptsAsync(DateOnly? from = null, DateOnly? to = null, bool? pendingOnly = null, string? reason = null, int? page = null, int? pageSize = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static string FakeJwt(string role)
    {
        static string B64(object o) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{B64(new { alg = "none" })}.{B64(new { sub = Guid.NewGuid().ToString(), role, name = "Entrada", gym_id = Guid.NewGuid().ToString(), exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds() })}.sig";
    }

    private (FakeAuthClient Auth, FakeAttendanceClient Attendance, InMemoryTokenStore Tokens) Setup(string? sessionRole = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var auth = new FakeAuthClient { AccessToken = FakeJwt(GsmRoles.Kiosk) };
        var attendance = new FakeAttendanceClient();
        var tokens = new InMemoryTokenStore();
        if (sessionRole is not null)
        {
            tokens.SetAsync(new AuthTokens(FakeJwt(sessionRole), "refresh", DateTimeOffset.UtcNow.AddMinutes(15))).AsTask().Wait();
        }

        Services.AddSingleton<IAuthClient>(auth);
        Services.AddSingleton<IAttendanceClient>(attendance);
        Services.AddSingleton<ITokenStore>(tokens);
        Services.AddSingleton<GsmAuthenticationStateProvider>();
        Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<GsmAuthenticationStateProvider>());
        return (auth, attendance, tokens);
    }

    [Fact]
    public void Kiosk_without_a_device_session_goes_to_pairing()
    {
        Setup();

        RenderComponent<Kiosk>();

        Assert.EndsWith("/kiosk/emparejar", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    [Fact]
    public void A_staff_session_does_not_open_the_kiosk_and_gets_a_message()
    {
        var (_, attendance, _) = Setup(GsmRoles.Staff);

        var cut = RenderComponent<Kiosk>();

        Assert.NotNull(cut.Find("[data-testid='kiosk-staff-message']"));
        Assert.Contains("sesión de personal", cut.Markup);
        Assert.Equal(0, attendance.Calls);
    }

    [Fact]
    public void The_owner_session_also_gets_the_message_and_is_not_paired_over()
    {
        Setup(GsmRoles.Owner);

        var cut = RenderComponent<KioskPair>();

        Assert.NotNull(cut.Find("[data-testid='kiosk-staff-message']"));
        Assert.Empty(cut.FindAll("[data-testid='kiosk-pair-submit']"));
    }

    [Fact]
    public async Task Pairing_with_the_code_stores_the_device_session_and_opens_the_kiosk()
    {
        var (auth, _, tokens) = Setup();
        var cut = RenderComponent<KioskPair>();

        cut.Find("input").Input("k7m2qx9p");
        cut.Find("[data-testid='kiosk-pair-submit']").Click();

        Assert.Equal("K7M2QX9P", auth.Paired!.PairingCode);
        Assert.Equal("device-refresh", (await tokens.GetAsync())!.RefreshToken);
        Assert.EndsWith("/kiosk", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    [Fact]
    public void A_revoked_kiosk_comes_back_to_pairing_with_a_notice()
    {
        Setup();
        Services.GetRequiredService<FakeNavigationManager>().NavigateTo("/kiosk/emparejar?revocado=1");

        var cut = RenderComponent<KioskPair>();

        Assert.Contains("desactivado", cut.Find("[data-testid='kiosk-revoked']").TextContent);
    }

    [Fact]
    public void Kiosk_shows_the_gym_name_and_logo_above_the_qr()
    {
        var (_, attendance, _) = Setup(GsmRoles.Kiosk);
        attendance.Branding = new PublicGymBrandingResponse { Name = "Iron Gym", LogoUrl = "https://cdn.test/logo.png", PrimaryColor = "#B50016", SecondaryColor = "#0AAFCA" };

        var cut = RenderComponent<Kiosk>();

        Assert.Equal("Iron Gym", cut.Find("[data-testid='kiosk-brand-name']").TextContent);
        Assert.Equal("https://cdn.test/logo.png", cut.Find("[data-testid='kiosk-brand-logo']").GetAttribute("src"));
    }

    [Fact]
    public void Kiosk_without_branding_still_opens()
    {
        Setup(GsmRoles.Kiosk);

        var cut = RenderComponent<Kiosk>();

        Assert.Empty(cut.FindAll("[data-testid='kiosk-brand']"));
        Assert.NotEmpty(cut.FindAll("svg"));
    }

    [Fact]
    public void Kiosk_with_a_device_session_shows_only_the_qr_and_the_welcome_without_member_names()
    {
        var (_, attendance, _) = Setup(GsmRoles.Kiosk);

        var cut = RenderComponent<Kiosk>();

        Assert.Equal(1, attendance.Calls);
        Assert.Contains("Bienvenido", cut.Markup);
        Assert.Contains("¿Problemas para ingresar?", cut.Markup);
        Assert.NotEmpty(cut.FindAll("svg"));
        // Sin panel: ni volver al panel ni listas de miembros.
        Assert.DoesNotContain("Volver al panel", cut.Markup);
        Assert.Empty(cut.FindAll("table"));
        Assert.Empty(cut.FindAll("a[href='/']"));
    }

    [Fact]
    public void Kiosk_with_the_qr_off_but_document_on_shows_the_keypad_without_the_qr()
    {
        var (_, attendance, _) = Setup(GsmRoles.Kiosk);
        attendance.ByDocument = true;
        attendance.QrDisabled = true;

        var cut = RenderComponent<Kiosk>();

        Assert.NotNull(cut.Find("[data-testid='kiosk-doc']"));
        Assert.Empty(cut.FindAll("[data-testid='kiosk-disabled']"));
        Assert.Empty(cut.FindAll(".gsm-kiosk__qr-card"));
    }

    [Fact]
    public void Kiosk_with_the_qr_feature_off_shows_a_message_without_links_to_the_panel()
    {
        var (_, attendance, _) = Setup(GsmRoles.Kiosk);
        attendance.FailWith = 403;

        var cut = RenderComponent<Kiosk>();

        Assert.NotNull(cut.Find("[data-testid='kiosk-disabled']"));
        Assert.Empty(cut.FindAll("a"));
    }

    [Fact]
    public void A_revoked_session_sends_the_kiosk_back_to_pairing()
    {
        var (_, attendance, _) = Setup(GsmRoles.Kiosk);
        attendance.FailWith = 401;

        RenderComponent<Kiosk>();

        Assert.EndsWith("/kiosk/emparejar?revocado=1", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    private static void Type(IRenderedComponent<Kiosk> cut, string digits)
    {
        foreach (var d in digits)
        {
            cut.Find($"button[data-testid='kiosk-key-{d}']").Click();
        }
    }

    [Fact]
    public void The_keypad_only_shows_when_the_gym_turned_on_document_entry()
    {
        var (_, attendance, _) = Setup(GsmRoles.Kiosk);

        Assert.Empty(RenderComponent<Kiosk>().FindAll("[data-testid='kiosk-doc']"));

        attendance.ByDocument = true;
        var cut = RenderComponent<Kiosk>();
        Assert.NotNull(cut.Find("[data-testid='kiosk-doc']"));
        Assert.Contains("Digita tu documento", cut.Markup);
    }

    [Fact]
    public void The_typed_number_is_masked_and_enter_needs_at_least_four_digits()
    {
        var (_, attendance, _) = Setup(GsmRoles.Kiosk);
        attendance.ByDocument = true;
        var cut = RenderComponent<Kiosk>();
        // La pantalla carga los ajustes en segundo plano: esperar el teclado antes de escribir.
        // Bajo carga (suite completa en paralelo) el render tarda más que el segundo por defecto de bUnit.
        var wait = TimeSpan.FromSeconds(10);
        cut.WaitForElement("button[data-testid='kiosk-key-enter']", wait);

        Type(cut, "123");
        cut.WaitForAssertion(() => Assert.True(cut.Find("button[data-testid='kiosk-key-enter']").HasAttribute("disabled")), wait);
        Type(cut, "4");
        cut.WaitForAssertion(() => Assert.Equal("••••", cut.Find("[data-testid='kiosk-doc-display']").TextContent), wait);

        var display = cut.Find("[data-testid='kiosk-doc-display']").TextContent;
        Assert.Equal("••••", display);
        Assert.DoesNotContain("1234", cut.Markup);
        Assert.False(cut.Find("button[data-testid='kiosk-key-enter']").HasAttribute("disabled"));
    }

    [Fact]
    public void A_valid_document_shows_only_the_first_name_and_the_expiring_notice_without_dates()
    {
        var (_, attendance, _) = Setup(GsmRoles.Kiosk);
        attendance.ByDocument = true;
        var cut = RenderComponent<Kiosk>();

        Type(cut, "1234567");
        cut.Find("button[data-testid='kiosk-key-enter']").Click();

        Assert.Equal(["1234567"], attendance.Documents);
        var welcome = cut.Find("[data-testid='kiosk-doc-welcome']").TextContent;
        Assert.Contains("Ana", welcome);
        Assert.Contains("Tu plan vence pronto, pasa por recepción", welcome);
        Assert.DoesNotMatch(@"\d{2}/\d{2}", welcome);
    }

    [Fact]
    public void Any_rejection_shows_the_same_generic_message()
    {
        var (_, attendance, _) = Setup(GsmRoles.Kiosk);
        attendance.ByDocument = true;
        attendance.DocumentFailWith = 409;
        var cut = RenderComponent<Kiosk>();

        Type(cut, "99999999");
        cut.Find("button[data-testid='kiosk-key-enter']").Click();

        Assert.Equal(
            "No pudimos registrar tu entrada. Comunícate con un asesor.",
            cut.Find("[data-testid='kiosk-doc-rejected']").TextContent);
        Assert.Equal("", cut.Find("[data-testid='kiosk-doc-display']").TextContent);
    }

    [Fact]
    public void A_429_locks_the_keypad()
    {
        var (_, attendance, _) = Setup(GsmRoles.Kiosk);
        attendance.ByDocument = true;
        attendance.DocumentFailWith = 429;
        var cut = RenderComponent<Kiosk>();

        Type(cut, "99999999");
        cut.Find("button[data-testid='kiosk-key-enter']").Click();

        Assert.NotNull(cut.Find("[data-testid='kiosk-doc-locked']"));
        Assert.True(cut.Find("button[data-testid='kiosk-key-5']").HasAttribute("disabled"));
        Assert.True(cut.Find("button[data-testid='kiosk-key-enter']").HasAttribute("disabled"));
    }
}
