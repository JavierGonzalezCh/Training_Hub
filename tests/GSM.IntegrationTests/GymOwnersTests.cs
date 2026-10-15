using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;
using ApiUser = GSM.API.Auth.CurrentUser;

namespace GSM.IntegrationTests;

/// <summary>
/// I4 + G4: varios Owners por gym, ascender un staff, quitar (el último no), traspaso y contacto de
/// facturación. Con identidad real (JWT) cuando importan los claims.
/// </summary>
public sealed class GymOwnersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    // ---- Ascender y agregar ----

    [Fact]
    public async Task Promoting_a_staff_makes_a_second_owner_with_every_permission()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var (staff, staffTokens) = await SignInClientAsync(host, w.StaffEmail);

        // Antes: staff de Recepción, sin permisos especiales y sin acceso a la gestión del equipo.
        Assert.Equal("staff", Role(staffTokens.AccessToken));
        Assert.Empty(Permissions(staffTokens.AccessToken));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/gym/staff")).StatusCode);

        var promote = await owner.PostAsync($"/api/gym/owners/{w.StaffId}/promote", null);
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        var promoted = (await promote.Content.ReadFromJsonAsync<GymOwnerResponse>())!;
        Assert.Equal(w.StaffId, promoted.UserId);
        Assert.True(promoted.IsActive);

        // Después: al volver a entrar ya es Owner con todos los permisos y gestiona el equipo.
        var (second, secondTokens) = await SignInClientAsync(host, w.StaffEmail);
        Assert.Equal("owner", Role(secondTokens.AccessToken));
        Assert.Equal(Enum.GetValues<StaffPermission>().Length, Permissions(secondTokens.AccessToken).Count);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/gym/staff")).StatusCode);

        var owners = (await (await owner.GetAsync("/api/gym/owners")).Content.ReadFromJsonAsync<List<GymOwnerResponse>>())!;
        Assert.Equal(2, owners.Count(o => o.IsActive));

        // Queda en el historial: quién, a quién, de qué perfil.
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var row = await db.StaffAccessAudits.AsNoTracking().SingleAsync(
            a => a.GymId == w.GymId && a.TargetUserId == w.StaffId && a.Action == StaffAccessAuditAction.OwnerPromoted);
        Assert.Equal(w.OwnerId, row.ActorUserId);
        Assert.Equal("reception", row.OldValue);

        // Sus ajustes de staff ya no significan nada.
        Assert.False(await db.GymAccessPermissions.AnyAsync(
            p => db.GymAccesses.Any(a => a.Id == p.GymAccessId && a.UserId == w.StaffId && a.GymId == w.GymId)));
    }

    [Fact]
    public async Task Only_an_active_staff_of_the_gym_can_be_promoted()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);

        // De otro gym: 404. Un Owner ya es Owner: 404 (no es staff).
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/gym/owners/{other.StaffId}/promote", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/gym/owners/{w.OwnerId}/promote", null)).StatusCode);

        // Un staff desactivado: 409.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/gym/staff/{w.StaffId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/gym/owners/{w.StaffId}/promote", null)).StatusCode);
    }

    [Fact]
    public async Task Inviting_an_owner_creates_a_pending_account_that_activates_with_the_emailed_link()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var email = NewEmail("nuevo");

        var invite = await owner.PostAsJsonAsync("/api/gym/owners", new InviteOwnerRequest(email, "Segundo Owner"));
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var created = (await invite.Content.ReadFromJsonAsync<GymOwnerResponse>())!;
        Assert.False(created.IsActivated);

        var link = EmailLinks.Parse(emails.LastTo(email)!);
        Assert.Equal("/activar", link.Path);
        Assert.NotNull(link.Code);

        var anon = host.CreateClient();
        var activate = await anon.PostAsJsonAsync("/api/auth/activate", new ActivateAccountRequest(email, link.Code!, "SegundoPass_123"));
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        var (second, tokens) = await SignInClientAsync(host, email);
        Assert.Equal("owner", Role(tokens.AccessToken));
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/gym/owners")).StatusCode);

        // Ya es Owner: invitarlo otra vez, 409. Un staff del gym: 409 (para eso está "ascender").
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsJsonAsync("/api/gym/owners", new InviteOwnerRequest(email, "Segundo Owner"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsJsonAsync("/api/gym/owners", new InviteOwnerRequest(w.StaffEmail, "Seguridad Staff"))).StatusCode);
    }

    [Fact]
    public async Task Only_an_owner_manages_owners()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/gym/owners")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsJsonAsync("/api/gym/owners", new InviteOwnerRequest(NewEmail("x"), "X"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/gym/owners/{w.StaffId}/promote", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"/api/gym/owners/{w.OwnerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/gym/billing-contact")).StatusCode);
    }

    // ---- Quitar, el último, traspaso ----

    [Fact]
    public async Task The_last_owner_cannot_be_removed_not_even_by_himself()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);

        var self = await owner.DeleteAsync($"/api/gym/owners/{w.OwnerId}");
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);

        var asStaff = await owner.DeleteAsync($"/api/gym/owners/{w.OwnerId}?keepAsStaff=true&staffProfile=trainer");
        Assert.Equal(HttpStatusCode.Conflict, asStaff.StatusCode);

        // Sigue siendo Owner y su sesión sigue viva.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/gym/owners")).StatusCode);
        Assert.True(await IsActiveOwnerAsync(w.GymId, w.OwnerId));
    }

    [Fact]
    public async Task Removing_an_owner_deactivates_the_access_and_revokes_his_sessions()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var second = await AddSecondOwnerAsync(w.GymId);
        var (_, secondTokens) = await SignInClientAsync(host, second.Email);

        // El primero quita al segundo.
        var removed = await owner.DeleteAsync($"/api/gym/owners/{second.UserId}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.False((await removed.Content.ReadFromJsonAsync<GymOwnerResponse>())!.IsActive);
        Assert.False(await IsActiveOwnerAsync(w.GymId, second.UserId));

        // Su sesión en este gym ya no renueva.
        var refresh = await host.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(secondTokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Quitar de nuevo es idempotente; un Owner de otro gym o inexistente, 404.
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/gym/owners/{second.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/gym/owners/{Guid.CreateVersion7()}")).StatusCode);

        // El quitado puede volver: invitarlo reactiva el acceso.
        var back = await owner.PostAsJsonAsync("/api/gym/owners", new InviteOwnerRequest(second.Email, "Segundo Owner"));
        Assert.Equal(HttpStatusCode.Created, back.StatusCode);
        Assert.True(await IsActiveOwnerAsync(w.GymId, second.UserId));
    }

    [Fact]
    public async Task Removing_an_owner_can_leave_him_as_staff_with_the_chosen_profile()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var second = await AddSecondOwnerAsync(w.GymId);

        var removed = await owner.DeleteAsync($"/api/gym/owners/{second.UserId}?keepAsStaff=true&staffProfile=trainer");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var access = await db.GymAccesses.AsNoTracking().SingleAsync(a => a.UserId == second.UserId && a.GymId == w.GymId && a.IsTeam);
        Assert.Equal(UserRole.Staff, access.Role);
        Assert.Equal(StaffProfile.Trainer, access.Profile);
        Assert.True(access.IsActive);

        // Y vuelve a entrar, ahora como staff.
        var (_, tokens) = await SignInClientAsync(host, second.Email);
        Assert.Equal("staff", Role(tokens.AccessToken));
    }

    [Fact]
    public async Task Two_owners_removing_each_other_at_once_never_leave_the_gym_without_an_owner()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var first = await LoggedInAsync(host, w.OwnerEmail);
        var second = await AddSecondOwnerAsync(w.GymId);
        var secondClient = await LoggedInAsync(host, second.Email);

        var results = await Task.WhenAll(
            first.DeleteAsync($"/api/gym/owners/{second.UserId}"),
            secondClient.DeleteAsync($"/api/gym/owners/{w.OwnerId}"));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var owners = await scope.ServiceProvider.GetRequiredService<IIdentityReader>().ListActiveOwnersAsync(w.GymId);
        Assert.Single(owners);
    }

    [Fact]
    public async Task Transferring_the_gym_adds_the_new_owner_then_removes_the_old_one_and_the_billing_contact_follows()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var oldOwner = await LoggedInAsync(host, w.OwnerEmail);
        var email = NewEmail("nuevo");

        // Paso 1 del flujo: agregar al nuevo dueño (que activa su cuenta).
        Assert.Equal(HttpStatusCode.Created,
            (await oldOwner.PostAsJsonAsync("/api/gym/owners", new InviteOwnerRequest(email, "Nuevo Dueño"))).StatusCode);
        var code = EmailLinks.Parse(emails.LastTo(email)!).Code!;
        Assert.Equal(HttpStatusCode.OK,
            (await host.CreateClient().PostAsJsonAsync("/api/auth/activate", new ActivateAccountRequest(email, code, "NuevoPass_12345"))).StatusCode);

        // Paso 2: quitarse a sí mismo, quedando como staff.
        var leave = await oldOwner.DeleteAsync($"/api/gym/owners/{w.OwnerId}?keepAsStaff=true");
        Assert.Equal(HttpStatusCode.OK, leave.StatusCode);

        // El contacto de facturación era el fundador: pasa al nuevo Owner.
        var newOwner = await LoggedInAsync(host, email);
        var contact = (await (await newOwner.GetAsync("/api/gym/billing-contact")).Content.ReadFromJsonAsync<BillingContactResponse>())!;
        Assert.Equal(email, contact.Email, ignoreCase: true);

        // El fundador ya no es Owner pero sigue en el gym como staff.
        Assert.False(await IsActiveOwnerAsync(w.GymId, w.OwnerId));
        var (_, tokens) = await SignInClientAsync(host, w.OwnerEmail);
        Assert.Equal("staff", Role(tokens.AccessToken));
    }

    // ---- Contacto de facturación ----

    [Fact]
    public async Task The_billing_contact_is_read_and_edited_and_must_be_an_active_owner()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var second = await AddSecondOwnerAsync(w.GymId);

        var initial = (await (await owner.GetAsync("/api/gym/billing-contact")).Content.ReadFromJsonAsync<BillingContactResponse>())!;
        Assert.Equal(w.OwnerId, initial.UserId);

        var set = await owner.PutAsJsonAsync("/api/gym/billing-contact", new SetBillingContactRequest(second.UserId));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var changed = (await set.Content.ReadFromJsonAsync<BillingContactResponse>())!;
        Assert.Equal(second.UserId, changed.UserId);
        Assert.Equal("Segundo Owner", changed.FullName);

        var persisted = (await (await owner.GetAsync("/api/gym/billing-contact")).Content.ReadFromJsonAsync<BillingContactResponse>())!;
        Assert.Equal(second.UserId, persisted.UserId);

        // Un staff o alguien de otro gym no puede ser el contacto: 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await owner.PutAsJsonAsync("/api/gym/billing-contact", new SetBillingContactRequest(w.StaffId))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await owner.PutAsJsonAsync("/api/gym/billing-contact", new SetBillingContactRequest(Guid.CreateVersion7()))).StatusCode);

        // Si se quita al contacto, pasa a otro Owner.
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/gym/owners/{second.UserId}")).StatusCode);
        var after = (await (await owner.GetAsync("/api/gym/billing-contact")).Content.ReadFromJsonAsync<BillingContactResponse>())!;
        Assert.Equal(w.OwnerId, after.UserId);
    }

    [Fact]
    public async Task Without_a_chosen_contact_it_reads_as_the_oldest_active_owner_and_reassignment_is_idempotent()
    {
        var w = await SeedAsync(setBillingContact: false);
        var second = await AddSecondOwnerAsync(w.GymId);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), w.GymId, UserRole.Owner);

        var contact = await scope.ServiceProvider.GetRequiredService<GetBillingContactHandler>().HandleAsync(default);
        Assert.Equal(w.OwnerId, contact.UserId);

        // Reintentar el evento de un Owner que no era el contacto no cambia nada.
        var handler = scope.ServiceProvider.GetServices<GSM.Application.Common.Events.IDomainEventHandler<GSM.Application.Identity.Events.GymOwnerRemoved>>()
            .OfType<ReassignBillingContactHandler>().Single();
        await handler.HandleAsync(new GSM.Application.Identity.Events.GymOwnerRemoved(w.GymId, second.UserId), default);
        await handler.HandleAsync(new GSM.Application.Identity.Events.GymOwnerRemoved(w.GymId, second.UserId), default);
        var again = await scope.ServiceProvider.GetRequiredService<GetBillingContactHandler>().HandleAsync(default);
        Assert.Equal(w.OwnerId, again.UserId);
    }

    // ---- SuperAdmin ----

    [Fact]
    public async Task The_super_admin_adds_and_removes_owners_of_any_gym_and_reads_the_billing_contact()
    {
        var w = await SeedAsync();
        var actorId = Guid.CreateVersion7();
        var email = NewEmail("soporte");

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(actorId, gymId: null, UserRole.SuperAdmin);

        var added = await scope.ServiceProvider.GetRequiredService<InviteOwnerHandler>()
            .HandleAsync(w.GymId, new InviteOwnerRequest(email, "Dueño de Soporte"), default);
        Assert.False(added.IsActivated);

        var contact = await scope.ServiceProvider.GetRequiredService<GetBillingContactHandler>().HandleAsync(w.GymId, default);
        Assert.Equal(w.OwnerId, contact.UserId);

        // Vendió el gym: el SuperAdmin quita al fundador; el contacto pasa al nuevo Owner.
        var remove = scope.ServiceProvider.GetRequiredService<RemoveOwnerHandler>();
        await remove.HandleAsync(w.GymId, w.OwnerId, keepAsStaff: false, staffProfile: null, default);
        var moved = await scope.ServiceProvider.GetRequiredService<GetBillingContactHandler>().HandleAsync(w.GymId, default);
        Assert.Equal(added.UserId, moved.UserId);

        // Ni siquiera el SuperAdmin deja el gym sin Owner.
        await Assert.ThrowsAsync<ConflictException>(
            () => remove.HandleAsync(w.GymId, added.UserId, keepAsStaff: false, staffProfile: null, default));

        using var audit = factory.CreateScopeWithMigration();
        audit.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = audit.ServiceProvider.GetRequiredService<GsmDbContext>();
        var actions = await db.StaffAccessAudits.AsNoTracking()
            .Where(a => a.GymId == w.GymId && a.ActorUserId == actorId)
            .Select(a => a.Action).ToListAsync();
        Assert.Contains(StaffAccessAuditAction.OwnerAdded, actions);
        Assert.Contains(StaffAccessAuditAction.OwnerRemoved, actions);
    }

    // ---- Infraestructura de los tests ----

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14] + "@test.local";

    private static string Role(string accessToken)
        => new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Claims.Single(c => c.Type == GsmClaims.Role).Value;

    private static List<string> Permissions(string accessToken)
        => new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Claims
            .Where(c => c.Type == GsmClaims.Permission).Select(c => c.Value).ToList();

    private static async Task<AuthTokensResponse> SignInAsync(HttpClient client, string email, Guid? gymId = null)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        if (!body.RequiresGymSelection)
        {
            return body.Tokens!;
        }

        var access = body.Accesses!.Single(a => a.GymId == gymId);
        var select = await client.PostAsJsonAsync("/api/auth/select-gym", new SelectGymRequest(body.SelectionToken!, access.AccessId));
        select.EnsureSuccessStatusCode();
        return (await select.Content.ReadFromJsonAsync<AuthTokensResponse>())!;
    }

    private static async Task<(HttpClient Client, AuthTokensResponse Tokens)> SignInClientAsync(
        WebApplicationFactory<Program> host, string email, Guid? gymId = null)
    {
        var client = host.CreateClient();
        var tokens = await SignInAsync(client, email, gymId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (client, tokens);
    }

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email, Guid? gymId = null)
        => (await SignInClientAsync(host, email, gymId)).Client;

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, ApiUser>();
        }));

    /// <summary>Un segundo Owner con contraseña ("Segundo Owner"), como si ya hubiera activado su cuenta.</summary>
    private async Task<(Guid UserId, string Email)> AddSecondOwnerAsync(Guid gymId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var email = NewEmail("segundo");
        var id = await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
            .CreateGymUserAsync(email, "Segundo Owner", Password, UserRole.Owner, gymId);
        return (id, email);
    }

    private async Task<bool> IsActiveOwnerAsync(Guid gymId, Guid userId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var owners = await scope.ServiceProvider.GetRequiredService<IIdentityReader>().ListActiveOwnersAsync(gymId);
        return owners.Any(o => o.UserId == userId);
    }

    private async Task<World> SeedAsync(bool setBillingContact = true)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Owners Gym", $"own-{suffix}");
        var gymB = Gym.Create("Owners Gym B", $"ownb-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), gymB, GymFeatureFlags.Default(gymB.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Owner Fundador", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Seguridad Staff", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        if (setBillingContact)
        {
            gym.SetBillingContact(ownerId);
        }

        await db.SaveChangesAsync();

        return new World(gym.Id, ownerId, ownerEmail, staffId, staffEmail);
    }

    private sealed record World(Guid GymId, Guid OwnerId, string OwnerEmail, Guid StaffId, string StaffEmail);
}
