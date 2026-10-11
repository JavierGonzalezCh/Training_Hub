using System.Security.Claims;
using Bunit;
using GSM.Client;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.Panel.UI.Pages.Settings;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

public class StaffPermissionsUiTests : TestContext
{
    private sealed class FakeStaffClient : IStaffClient
    {
        public UpdateStaffPermissionsRequest? Saved { get; private set; }

        public Task<ICollection<StaffResponse>> StaffAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StaffResponse> StaffPOSTAsync(CreateStaffRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ICollection<StaffProfileInfo>> ProfilesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ICollection<StaffAuditResponse>> AuditAsync(Guid? userId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StaffDELETEAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountLinkResponse> Link2Async(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StaffResponse> Reactivate2Async(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StaffResponse> PermissionsAsync(Guid id, UpdateStaffPermissionsRequest body, CancellationToken cancellationToken = default)
        {
            Saved = body;
            return Task.FromResult(new StaffResponse
            {
                Id = id, Email = "e@t.l", FullName = "Carlos", IsActive = true,
                Profile = body.Profile, IsAdjusted = true, Permissions = body.Permissions,
            });
        }
    }

    private static readonly List<StaffProfileInfo> Profiles =
    [
        new() { Profile = "reception", Permissions = [] },
        new() { Profile = "trainer", Permissions = ["trainer", "view_member_health"] },
        new() { Profile = "administrator", Permissions = ["view_member_health", "manage_attendance", "void_payments", "view_finances"] },
    ];

    private static StaffResponse Trainer() => new()
    {
        Id = Guid.NewGuid(), Email = "c@t.l", FullName = "Carlos", IsActive = true,
        Profile = "trainer", IsAdjusted = false, Permissions = ["trainer", "view_member_health"],
    };

    private async Task<(FakeStaffClient Api, IRenderedComponent<MudDialogProvider> Provider)> OpenAsync()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var api = new FakeStaffClient();
        Services.AddSingleton<IStaffClient>(api);

        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<StaffPermissionsDialog>("Permisos", new DialogParameters<StaffPermissionsDialog>
        {
            { x => x.Staff, Trainer() },
            { x => x.Profiles, Profiles },
        }));
        return (api, provider);
    }

    [Fact]
    public async Task Dialog_starts_from_the_current_permissions_with_save_disabled()
    {
        var (_, provider) = await OpenAsync();

        var boxes = provider.FindAll("input[type=checkbox]");
        Assert.Equal(5, boxes.Count);
        Assert.Equal(2, boxes.Count(b => b.HasAttribute("checked")));
        Assert.True(provider.Find("button.mud-button-filled").HasAttribute("disabled"));
        Assert.Empty(provider.FindAll("#revoke-warning"));
    }

    [Fact]
    public async Task Unchecking_a_permission_warns_that_sessions_close_and_saving_sends_the_choice()
    {
        var (api, provider) = await OpenAsync();

        // Quita "Ver y editar datos de salud".
        provider.FindAll("input[type=checkbox]")[0].Change(false);

        Assert.Contains("cierra las sesiones", provider.Find("#revoke-warning").TextContent);

        provider.Find("button.mud-button-filled").Click();

        Assert.Equal("trainer", api.Saved!.Profile);
        Assert.Equal(["trainer"], api.Saved.Permissions.ToArray());
    }

    [Fact]
    public async Task Adding_a_permission_does_not_warn_about_sessions()
    {
        var (_, provider) = await OpenAsync();

        provider.FindAll("input[type=checkbox]")[2].Change(true);

        Assert.Empty(provider.FindAll("#revoke-warning"));
        Assert.False(provider.Find("button.mud-button-filled").HasAttribute("disabled"));
    }

    [Fact]
    public void Adjusted_staff_shows_the_adjusted_suffix()
    {
        Assert.Equal("Entrenador (ajustado)", StaffLabels.ProfileLabel("trainer", true));
        Assert.Equal("Entrenador", StaffLabels.ProfileLabel("trainer", false));
        Assert.Equal("Administrador", StaffLabels.ProfileLabel("administrator", false));
    }

    [Fact]
    public void Audit_text_names_actor_target_and_change()
    {
        Assert.Equal(
            "Ana le quitó a Luis el permiso «Ver finanzas»",
            StaffLabels.AuditText("permission_revoked", "Ana", "Luis", "view_finances", null, null));
    }

    [Fact]
    public void Jwt_payload_reads_permissions_array()
    {
        var body = Convert.ToBase64String("""{"sub":"u","role":"staff","perm":["trainer","view_finances"]}"""u8.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var payload = JwtPayload.TryRead($"h.{body}.s");

        Assert.Equal(["trainer", "view_finances"], payload!.Permissions!.ToArray());
    }

    [Fact]
    public void HasPermission_follows_role_and_claims()
    {
        static ClaimsPrincipal User(string role, params string[] perms) => new(new ClaimsIdentity(
            perms.Select(p => new Claim("perm", p)).Append(new Claim(ClaimTypes.Role, role)),
            "test", ClaimTypes.Name, ClaimTypes.Role));

        Assert.True(User("owner").HasPermission(GsmPermissions.ViewFinances));
        Assert.True(User("staff", "view_finances").HasPermission(GsmPermissions.ViewFinances));
        Assert.False(User("staff", "trainer").HasPermission(GsmPermissions.ViewFinances));
        Assert.False(User("member", "view_finances").HasPermission(GsmPermissions.ViewFinances));
    }
}
