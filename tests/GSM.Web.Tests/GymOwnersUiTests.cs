using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.Panel.UI.Pages.Settings;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Formatting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>I4 + G4: Owners del gym en Configuración → Staff (agregar, contacto de facturación, quitar, traspasar).</summary>
public class GymOwnersUiTests : TestContext
{
    /// <summary>Cliente de Owners sin red: devuelve la lista y registra las llamadas (los nombres son los del cliente generado).</summary>
    public class OwnersStub : DispatchProxy
    {
        public List<GymOwnerResponse> Owners { get; set; } = [];
        public List<string> Calls { get; } = [];
        public List<InviteOwnerRequest> Invited { get; } = [];
        public List<(Guid UserId, bool? KeepAsStaff, string? Profile)> Removed { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            Calls.Add(name);
            object? result = null;

            switch (name)
            {
                case "OwnersAll2Async":
                    result = (ICollection<GymOwnerResponse>)Owners.ToList();
                    break;
                case "OwnersPOST2Async":
                    var request = (InviteOwnerRequest)args![0]!;
                    Invited.Add(request);
                    result = new GymOwnerResponse { UserId = Guid.NewGuid(), Email = request.Email, FullName = request.FullName, IsActive = true, IsActivated = false };
                    break;
                case "OwnersDELETE2Async":
                    Removed.Add(((Guid)args![0]!, (bool?)args[1], (string?)args[2]));
                    result = new GymOwnerResponse { UserId = (Guid)args[0]!, Email = "x@t.l", FullName = "X", IsActive = false, IsActivated = true };
                    break;
            }

            return AsTask(targetMethod.ReturnType, result);
        }
    }

    /// <summary>Cliente del gym sin red: solo el contacto de facturación.</summary>
    public class GymStub : DispatchProxy
    {
        public Guid? ContactId { get; set; }
        public List<SetBillingContactRequest> Set { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result = null;
            switch (targetMethod!.Name)
            {
                case "BillingContactGET2Async":
                    result = new BillingContactResponse { UserId = ContactId };
                    break;
                case "BillingContactPUTAsync":
                    var request = (SetBillingContactRequest)args![0]!;
                    Set.Add(request);
                    ContactId = request.UserId;
                    result = new BillingContactResponse { UserId = request.UserId };
                    break;
            }

            return AsTask(targetMethod.ReturnType, result);
        }
    }

    public class AuthStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => AsTask(targetMethod!.ReturnType, null);
    }

    private static object AsTask(Type returnType, object? result)
    {
        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        var inner = returnType.GetGenericArguments()[0];
        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result])!;
    }

    private static readonly Guid Me = Guid.NewGuid();

    private static GymOwnerResponse Owner(Guid id, string name, bool activated = true) => new()
    {
        UserId = id, FullName = name, Email = $"{name.ToLowerInvariant()}@t.l", IsActive = true, IsActivated = activated,
    };

    private (OwnersStub Owners, GymStub Gym) Setup(Guid? contactId, params GymOwnerResponse[] owners)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var ownersApi = DispatchProxy.Create<IOwnersClient, OwnersStub>();
        var stub = (OwnersStub)(object)ownersApi;
        stub.Owners = owners.ToList();
        var gymApi = DispatchProxy.Create<IGymClient, GymStub>();
        var gymStub = (GymStub)(object)gymApi;
        gymStub.ContactId = contactId;

        Services.AddSingleton(ownersApi);
        Services.AddSingleton(gymApi);

        // Lo que necesitan los diálogos que cierran la sesión.
        var auth = DispatchProxy.Create<IAuthClient, AuthStub>();
        var tokens = new InMemoryTokenStore();
        Services.AddSingleton(auth);
        Services.AddSingleton<ITokenStore>(tokens);
        Services.AddSingleton<GsmAuthenticationStateProvider>();
        Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<GsmAuthenticationStateProvider>());
        var context = new GymContext();
        context.Set(new MeResponse { UserId = Me, FullName = "Yo", Email = "yo@t.l", Role = "owner" }, impersonating: false);
        Services.AddSingleton(context);
        Services.AddSingleton<SessionService>();
        return (stub, gymStub);
    }

    [Fact]
    public void The_billing_contact_is_marked_and_the_others_can_become_it()
    {
        var ana = Owner(Guid.NewGuid(), "Ana");
        var luis = Owner(Guid.NewGuid(), "Luis");
        Setup(ana.UserId, ana, luis);

        var cut = RenderComponent<OwnersSection>();

        Assert.Single(cut.FindAll("[data-testid='billing-contact']"));
        // Solo quien no es contacto ofrece "Marcar como contacto".
        Assert.Single(cut.FindAll("[data-testid='set-contact']"));
        Assert.Contains("Ana", cut.Find("[data-testid='billing-contact']").Closest("td")!.TextContent);
        // Cada Owner activo se puede quitar.
        Assert.Equal(2, cut.FindAll("[data-testid='remove-owner']").Count);
    }

    [Fact]
    public void Marking_another_owner_as_contact_sends_him_and_moves_the_chip()
    {
        var ana = Owner(Guid.NewGuid(), "Ana");
        var luis = Owner(Guid.NewGuid(), "Luis");
        var (_, gym) = Setup(ana.UserId, ana, luis);

        var cut = RenderComponent<OwnersSection>();
        cut.Find("[data-testid='set-contact']").Click();

        Assert.Equal(luis.UserId, Assert.Single(gym.Set).UserId);
        cut.WaitForAssertion(() =>
            Assert.Contains("Luis", cut.Find("[data-testid='billing-contact']").Closest("td")!.TextContent));
    }

    [Fact]
    public void Adding_an_owner_sends_the_typed_name_and_email_and_validates_them()
    {
        var ana = Owner(Guid.NewGuid(), "Ana");
        var (owners, _) = Setup(ana.UserId, ana);

        var cut = RenderComponent<OwnersSection>();

        // Vacío: no llama a la API.
        cut.Find("[data-testid='add-owner']").Click();
        Assert.Empty(owners.Invited);

        cut.Find("input[data-testid='owner-name']").Change("Nuevo Dueño");
        cut.Find("input[data-testid='owner-email']").Change("nuevo@t.l");
        cut.Find("[data-testid='add-owner']").Click();

        var sent = Assert.Single(owners.Invited);
        Assert.Equal("Nuevo Dueño", sent.FullName);
        Assert.Equal("nuevo@t.l", sent.Email);
    }

    [Fact]
    public async Task Removing_an_owner_asks_whether_to_keep_him_as_staff()
    {
        var ana = Owner(Me, "Ana");
        var luis = Owner(Guid.NewGuid(), "Luis");
        var (owners, _) = Setup(ana.UserId, ana, luis);

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var cut = RenderComponent<OwnersSection>();

        // El botón de Luis (el segundo); Ana es el usuario actual.
        await cut.InvokeAsync(() => cut.FindAll("[data-testid='remove-owner']")[1].Click());
        provider.WaitForAssertion(() => provider.Find("[data-testid='keep-as-staff']"));
        provider.Find("input[data-testid='keep-as-staff']").Change(true);
        provider.Find("[data-testid='confirm-remove-owner']").Click();

        var removed = Assert.Single(owners.Removed);
        Assert.Equal(luis.UserId, removed.UserId);
        Assert.True(removed.KeepAsStaff);
        Assert.Equal("reception", removed.Profile);
    }

    [Fact]
    public async Task Transferring_adds_the_new_owner_first_and_then_removes_oneself()
    {
        var ana = Owner(Me, "Ana");
        var (owners, _) = Setup(ana.UserId, ana);
        var nav = Services.GetRequiredService<FakeNavigationManager>();

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var cut = RenderComponent<OwnersSection>();

        await cut.InvokeAsync(() => cut.Find("[data-testid='transfer-gym']").Click());
        provider.WaitForAssertion(() => provider.Find("[data-testid='confirm-transfer']"));

        provider.Find("input[data-testid='transfer-name']").Change("Nuevo Dueño");
        provider.Find("input[data-testid='transfer-email']").Change("nuevo@t.l");
        provider.Find("input[data-testid='transfer-keep']").Change(true);
        provider.Find("[data-testid='confirm-transfer']").Click();

        provider.WaitForAssertion(() => Assert.Single(owners.Removed));

        // Primero se agrega al nuevo (así el gym nunca se queda sin Owner) y después se quita uno mismo.
        Assert.Equal("nuevo@t.l", Assert.Single(owners.Invited).Email);
        Assert.True(owners.Calls.IndexOf("OwnersPOST2Async") < owners.Calls.IndexOf("OwnersDELETE2Async"));
        var removed = Assert.Single(owners.Removed);
        Assert.Equal(Me, removed.UserId);
        Assert.True(removed.KeepAsStaff);

        // La sesión se cierra: sale al login.
        provider.WaitForAssertion(() => Assert.EndsWith("/login", nav.Uri));
    }

    [Fact]
    public async Task Promoting_a_staff_asks_for_confirmation_with_the_staff_row_button()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var staffApi = DispatchProxy.Create<IStaffClient, StaffStub>();
        var stub = (StaffStub)(object)staffApi;
        var carlos = new StaffResponse
        {
            Id = Guid.NewGuid(), Email = "c@t.l", FullName = "Carlos", IsActive = true, Profile = "reception",
            IsAdjusted = false, Permissions = [],
        };
        stub.Staff = [carlos];
        Services.AddSingleton(staffApi);

        var ownersApi = DispatchProxy.Create<IOwnersClient, OwnersStub>();
        var ownersStub = (OwnersStub)(object)ownersApi;
        Services.AddSingleton(ownersApi);
        Services.AddSingleton(DispatchProxy.Create<IGymClient, GymStub>());
        var context = new GymContext();
        context.Set(new MeResponse { UserId = Me, FullName = "Yo", Email = "yo@t.l", Role = "owner" }, impersonating: false);
        Services.AddSingleton(context);
        Services.AddSingleton(DispatchProxy.Create<IAuthClient, AuthStub>());
        Services.AddSingleton<ITokenStore>(new InMemoryTokenStore());
        Services.AddSingleton<GsmAuthenticationStateProvider>();
        Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<GsmAuthenticationStateProvider>());
        Services.AddSingleton<SessionService>();
        Services.AddSingleton<GSM.UI.Shared.IClipboard>(new NoClipboard());

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var cut = RenderComponent<StaffTab>();

        // Solo el staff activo ofrece "Ascender a Owner".
        Assert.Single(cut.FindAll("[data-testid='promote-staff']"));
        await cut.InvokeAsync(() => cut.Find("[data-testid='promote-staff']").Click());
        provider.WaitForAssertion(() => provider.Find(".mud-message-box__yes-button"));
        provider.Find(".mud-message-box__yes-button").Click();

        provider.WaitForAssertion(() => Assert.Contains("PromoteAsync", ownersStub.Calls));
    }

    private sealed class NoClipboard : GSM.UI.Shared.IClipboard
    {
        public Task<bool> CopyAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    public class StaffStub : DispatchProxy
    {
        public List<StaffResponse> Staff { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => AsTask(
                targetMethod!.ReturnType,
                targetMethod.Name == "StaffAllAsync" ? (ICollection<StaffResponse>)Staff.ToList() : null);
    }

    [Fact]
    public void Owner_changes_read_as_sentences_in_the_audit_history()
    {
        Assert.Equal("Ana agregó a Luis como Owner", StaffLabels.AuditText("owner_added", "Ana", "Luis", null, null, null));
        Assert.Equal("Ana quitó a Luis como Owner", StaffLabels.AuditText("owner_removed", "Ana", "Luis", null, null, null));
        Assert.Contains("a Owner", StaffLabels.AuditText("owner_promoted", "Ana", "Luis", null, "reception", null));
        Assert.Contains("como staff", StaffLabels.AuditText("owner_demoted", "Ana", "Luis", null, null, "trainer"));
    }
}
