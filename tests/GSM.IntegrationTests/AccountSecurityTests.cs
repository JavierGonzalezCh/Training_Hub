using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ApiUser = GSM.API.Auth.CurrentUser;

namespace GSM.IntegrationTests;

/// <summary>
/// I1: bloqueo por intentos, cambio de contraseña con sesión, enlaces de restablecer y activar, y "copiar
/// enlace". Con identidad real (JWT) y el reloj de tests (<see cref="MutableClock"/>) para los vencimientos.
/// </summary>
public sealed class AccountSecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    // ---- Bloqueo por intentos (§7) ----

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one_until_fifteen_minutes_pass()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var clock = host.Services.GetRequiredService<MutableClock>();
        var client = host.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var bad = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, "Wrong_Pass_999"));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }

        // El aviso sale al bloquearse, una sola vez.
        Assert.Single(emails.Sent, m => IsLockoutNotice(m, w.OwnerEmail));

        // El 6.º intento, aun con la contraseña buena, falla con el mismo mensaje genérico.
        var locked = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, Password));
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        var reference = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, "Wrong_Pass_999"));
        Assert.Equal(await DetailAsync(reference), await DetailAsync(locked));
        Assert.Single(emails.Sent, m => IsLockoutNotice(m, w.OwnerEmail));

        try
        {
            // A los 14 minutos sigue bloqueada; pasados los 15, entra.
            clock.PinTo(DateTimeOffset.UtcNow.AddMinutes(14), DateOnly.FromDateTime(DateTime.UtcNow));
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, Password))).StatusCode);

            clock.PinTo(DateTimeOffset.UtcNow.AddMinutes(16), DateOnly.FromDateTime(DateTime.UtcNow));
            var ok = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, Password));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
        finally
        {
            clock.Unpin();
        }
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_answer_with_the_same_status_and_message()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var client = host.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, "Wrong_Pass_999"));
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nadie@nowhere.test", "Wrong_Pass_999"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, unknownEmail.StatusCode);
        Assert.Equal(await DetailAsync(wrongPassword), await DetailAsync(unknownEmail));
    }

    [Fact]
    public async Task A_correct_login_clears_the_failed_attempts_counter()
    {
        var w = await SeedAsync();

        // Directo al servicio: el límite por IP del endpoint (10/min) no deja hacer tantos intentos por HTTP.
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var credentials = scope.ServiceProvider.GetRequiredService<IUserCredentialsService>();

        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < 4; i++)
            {
                var bad = await credentials.SignInAsync(w.OwnerEmail, "Wrong_Pass_999");
                Assert.Null(bad.Account);
                Assert.False(bad.LockedOutNow);
            }

            Assert.NotNull((await credentials.SignInAsync(w.OwnerEmail, Password)).Account);
        }
    }

    // ---- Cambiar la contraseña con sesión (§11) ----

    [Fact]
    public async Task Changing_the_password_closes_the_other_sessions_in_every_gym_and_keeps_the_current_one()
    {
        var w = await SeedAsync(extraGymForOwner: true);

        string current;
        string otherSameGym;
        string otherGym;
        string accessToken;
        await using (var host = RealIdentityHost())
        {
            var anonymous = host.CreateClient();
            otherSameGym = (await SignInAsync(anonymous, w.OwnerEmail, Password, w.GymA)).RefreshToken;
            otherGym = (await SignInAsync(anonymous, w.OwnerEmail, Password, w.GymB)).RefreshToken;
            var session = await SignInAsync(anonymous, w.OwnerEmail, Password, w.GymA);
            current = session.RefreshToken;
            accessToken = session.AccessToken;

            var client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var change = await client.PostAsJsonAsync(
                "/api/me/password", new ChangePasswordRequest(Password, "NewPass_67890", current));
            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        }

        // Host nuevo = límite por IP limpio.
        await using var after = RealIdentityHost();
        var anon = after.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(anon, otherSameGym)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(anon, otherGym)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(anon, current)).StatusCode);

        // La contraseña vieja ya no entra; la nueva sí.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await anon.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, "NewPass_67890"))).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_without_the_current_refresh_token_closes_every_session()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();
        var first = await SignInAsync(anonymous, w.OwnerEmail, Password);
        var second = await SignInAsync(anonymous, w.OwnerEmail, Password);

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/me/password", new ChangePasswordRequest(Password, "NewPass_67890"))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(anonymous, first.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(anonymous, second.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task A_wrong_current_password_is_a_422_and_leaves_every_session_open()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();
        var other = await SignInAsync(anonymous, w.OwnerEmail, Password);
        var session = await SignInAsync(anonymous, w.OwnerEmail, Password);

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var change = await client.PostAsJsonAsync(
            "/api/me/password", new ChangePasswordRequest("Wrong_Pass_999", "NewPass_67890", session.RefreshToken));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(anonymous, other.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, Password))).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_requires_a_session()
    {
        await using var host = RealIdentityHost();
        var change = await host.CreateClient().PostAsJsonAsync(
            "/api/me/password", new ChangePasswordRequest(Password, "NewPass_67890"));
        Assert.Equal(HttpStatusCode.Unauthorized, change.StatusCode);
    }

    // ---- Restablecer por enlace (§4) ----

    [Fact]
    public async Task The_reset_email_carries_a_link_with_email_and_code_and_resetting_closes_all_sessions()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var anonymous = host.CreateClient();
        var session = await SignInAsync(anonymous, w.OwnerEmail, Password);

        Assert.Equal(HttpStatusCode.Accepted,
            (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(w.OwnerEmail))).StatusCode);

        var link = EmailLinks.Parse(emails.LastTo(w.OwnerEmail)!);
        Assert.Equal("/restablecer", link.Path);
        Assert.Equal(w.OwnerEmail, link.Email);
        Assert.False(string.IsNullOrEmpty(link.Code));

        var reset = await anonymous.PostAsJsonAsync(
            "/api/auth/reset-password", new ResetPasswordRequest(link.Email, link.Code!, "NewPass_67890"));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // Cerró todas las sesiones.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(anonymous, session.RefreshToken)).StatusCode);

        // El enlace es de un solo uso.
        var again = await anonymous.PostAsJsonAsync(
            "/api/auth/reset-password", new ResetPasswordRequest(link.Email, link.Code!, "Other_Pass_12345"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, "NewPass_67890"))).StatusCode);
    }

    [Fact]
    public async Task The_reset_link_expires_after_24_hours()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var clock = host.Services.GetRequiredService<MutableClock>();
        var anonymous = host.CreateClient();

        await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(w.OwnerEmail));
        var link = EmailLinks.Parse(emails.LastTo(w.OwnerEmail)!);

        try
        {
            clock.PinTo(DateTimeOffset.UtcNow.AddHours(25), DateOnly.FromDateTime(DateTime.UtcNow));
            var late = await anonymous.PostAsJsonAsync(
                "/api/auth/reset-password", new ResetPasswordRequest(link.Email, link.Code!, "NewPass_67890"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, late.StatusCode);

            clock.PinTo(DateTimeOffset.UtcNow.AddHours(23), DateOnly.FromDateTime(DateTime.UtcNow));
            var inTime = await anonymous.PostAsJsonAsync(
                "/api/auth/reset-password", new ResetPasswordRequest(link.Email, link.Code!, "NewPass_67890"));
            Assert.Equal(HttpStatusCode.NoContent, inTime.StatusCode);
        }
        finally
        {
            clock.Unpin();
        }
    }

    [Fact]
    public async Task Resetting_the_password_lifts_the_lockout()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, "Wrong_Pass_999"));
        }

        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(w.OwnerEmail));
        var link = EmailLinks.Parse(emails.Sent.Last(m => m.To == w.OwnerEmail && m.TextBody!.Contains("/restablecer")));
        (await client.PostAsJsonAsync(
            "/api/auth/reset-password", new ResetPasswordRequest(link.Email, link.Code!, "NewPass_67890")))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, "NewPass_67890"))).StatusCode);
    }

    // ---- Activar por enlace (§4) ----

    [Fact]
    public async Task Activating_with_the_staff_link_sets_the_password_and_starts_a_session_once()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var email = NewEmail("nuevo");

        (await owner.PostAsJsonAsync("/api/gym/staff", new CreateStaffRequest(email, "Nueva Persona")))
            .EnsureSuccessStatusCode();
        var link = EmailLinks.Parse(emails.LastTo(email)!);
        Assert.Equal("/activar", link.Path);
        Assert.Equal(email, link.Email);

        var anonymous = host.CreateClient();
        var activated = await anonymous.PostAsJsonAsync(
            "/api/auth/activate", new ActivateAccountRequest(link.Email, link.Code!, "StaffPass_12345"));
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var login = (await activated.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(login.RequiresGymSelection);
        Assert.False(string.IsNullOrEmpty(login.Tokens!.AccessToken));

        // La sesión que dejó la activación sirve.
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(anonymous, login.Tokens.RefreshToken)).StatusCode);

        // Un solo uso: el mismo enlace ya no activa nada ni pisa la contraseña.
        var again = await anonymous.PostAsJsonAsync(
            "/api/auth/activate", new ActivateAccountRequest(link.Email, link.Code!, "Hijack_Pass_12345"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "StaffPass_12345"))).StatusCode);
    }

    [Fact]
    public async Task The_activation_link_lasts_72_hours_not_24()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var clock = host.Services.GetRequiredService<MutableClock>();
        var email = NewEmail("tarde");
        (await owner.PostAsJsonAsync("/api/gym/staff", new CreateStaffRequest(email, "Tarde Persona")))
            .EnsureSuccessStatusCode();
        var link = EmailLinks.Parse(emails.LastTo(email)!);
        var anonymous = host.CreateClient();

        try
        {
            clock.PinTo(DateTimeOffset.UtcNow.AddHours(73), DateOnly.FromDateTime(DateTime.UtcNow));
            Assert.Equal(HttpStatusCode.UnprocessableEntity,
                (await anonymous.PostAsJsonAsync("/api/auth/activate",
                    new ActivateAccountRequest(link.Email, link.Code!, "StaffPass_12345"))).StatusCode);

            clock.PinTo(DateTimeOffset.UtcNow.AddHours(71), DateOnly.FromDateTime(DateTime.UtcNow));
            Assert.Equal(HttpStatusCode.OK,
                (await anonymous.PostAsJsonAsync("/api/auth/activate",
                    new ActivateAccountRequest(link.Email, link.Code!, "StaffPass_12345"))).StatusCode);
        }
        finally
        {
            clock.Unpin();
        }
    }

    [Fact]
    public async Task Inviting_someone_who_already_has_an_account_sends_a_link_to_log_in_instead_of_a_code()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();

        // El dueño de otro gym ya tiene cuenta con contraseña.
        var other = await SeedAsync();
        (await owner.PostAsJsonAsync("/api/gym/staff", new CreateStaffRequest(other.OwnerEmail, "Otro Dueño")))
            .EnsureSuccessStatusCode();

        var link = EmailLinks.Parse(emails.LastTo(other.OwnerEmail)!);
        Assert.Equal("/activar", link.Path);
        Assert.Null(link.Code);

        // Sin código no hay forma de crearle otra contraseña.
        var anonymous = host.CreateClient();
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await anonymous.PostAsJsonAsync("/api/auth/activate",
                new ActivateAccountRequest(other.OwnerEmail, "codigo-inventado", "Hijack_Pass_12345"))).StatusCode);
    }

    // ---- Copiar enlace (§4) ----

    [Fact]
    public async Task The_owner_copies_a_staff_link_that_works_and_the_copy_is_recorded()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        var email = NewEmail("copia");
        var staff = (await (await owner.PostAsJsonAsync("/api/gym/staff", new CreateStaffRequest(email, "Copia Staff")))
            .Content.ReadFromJsonAsync<StaffResponse>())!;

        var copied = await owner.PostAsync($"/api/gym/staff/{staff.Id}/link", null);
        Assert.Equal(HttpStatusCode.OK, copied.StatusCode);
        var link = (await copied.Content.ReadFromJsonAsync<AccountLinkResponse>())!;
        Assert.False(link.RequiresLogin);
        Assert.NotNull(link.ExpiresAt);
        Assert.Contains("/activar?email=", link.Url);

        // Registrado: quién lo copió.
        var audit = (await owner.GetFromJsonAsync<List<StaffAuditResponse>>($"/api/gym/staff/audit?userId={staff.Id}"))!;
        var entry = Assert.Single(audit, a => a.Action == "link_copied");
        Assert.Equal(w.OwnerId, entry.ActorUserId);
        Assert.Equal(staff.Id, entry.TargetUserId);

        // El enlace copiado activa la cuenta.
        var code = System.Web.HttpUtility.ParseQueryString(new Uri("http://x" + link.Url).Query)["code"]!;
        Assert.Equal(HttpStatusCode.OK,
            (await host.CreateClient().PostAsJsonAsync("/api/auth/activate",
                new ActivateAccountRequest(email, code, "StaffPass_12345"))).StatusCode);

        // Ya activada: el enlace nuevo la lleva a entrar, sin código.
        var afterwards = (await (await owner.PostAsync($"/api/gym/staff/{staff.Id}/link", null))
            .Content.ReadFromJsonAsync<AccountLinkResponse>())!;
        Assert.True(afterwards.RequiresLogin);
        Assert.Null(afterwards.ExpiresAt);
        Assert.DoesNotContain("code=", afterwards.Url);
    }

    [Fact]
    public async Task Copying_a_link_is_for_the_owner_of_that_gym_only()
    {
        var mine = await SeedAsync();
        var other = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, mine.OwnerEmail, Password);
        var staff = await LoggedInAsync(host, mine.StaffEmail, Password);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/gym/staff/{mine.StaffId}/link", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/gym/staff/{other.StaffId}/link", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/gym/staff/{mine.OwnerId}/link", null)).StatusCode);

        // Los owners del gym los copia solo el SuperAdmin.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.PostAsync($"/api/admin/gyms/{mine.GymA}/owners/{mine.OwnerId}/link", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/admin/gyms/{mine.GymA}/owners")).StatusCode);
    }

    [Fact]
    public async Task The_super_admin_lists_owners_and_copies_an_activation_link_that_is_recorded()
    {
        var w = await SeedAsync();
        var pending = await SeedPendingOwnerAsync(w.GymA);
        var actorId = Guid.CreateVersion7();

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(actorId, gymId: null, UserRole.SuperAdmin);

        var owners = await scope.ServiceProvider.GetRequiredService<ListGymOwnersHandler>().HandleAsync(w.GymA, default);
        Assert.Contains(owners, o => o.UserId == w.OwnerId && o.IsActivated);
        Assert.Contains(owners, o => o.UserId == pending.UserId && !o.IsActivated);

        var copy = scope.ServiceProvider.GetRequiredService<CopyOwnerLinkHandler>();

        var activation = await copy.HandleAsync(w.GymA, pending.UserId, default);
        Assert.False(activation.RequiresLogin);
        Assert.Contains("code=", activation.Url);

        var login = await copy.HandleAsync(w.GymA, w.OwnerId, default);
        Assert.True(login.RequiresLogin);

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var rows = await db.StaffAccessAudits.AsNoTracking()
            .Where(a => a.ActorUserId == actorId && a.Action == StaffAccessAuditAction.LinkCopied)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(w.GymA, r.GymId));

        // El enlace de activación crea la contraseña del owner pendiente.
        var code = System.Web.HttpUtility.ParseQueryString(new Uri("http://x" + activation.Url).Query)["code"]!;
        var account = await scope.ServiceProvider.GetRequiredService<IPasswordResetService>()
            .ActivateAsync(pending.Email, code, "OwnerNew_12345");
        Assert.Equal(pending.UserId, account.Id);

        // Un gym ajeno o una persona sin ese acceso: 404.
        await Assert.ThrowsAsync<GSM.Domain.Common.Exceptions.NotFoundException>(
            () => copy.HandleAsync(Guid.CreateVersion7(), pending.UserId, default));
    }

    // ---- Infraestructura de los tests ----

    private static bool IsLockoutNotice(GSM.Application.Common.Abstractions.EmailMessage m, string to)
        => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase) && m.HtmlBody.Contains("Hubo 5 intentos fallidos");

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14] + "@test.local";

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken)
        => client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(refreshToken));

    /// <summary>Login + (si hay varios gyms) selección del gym indicado.</summary>
    private static async Task<AuthTokensResponse> SignInAsync(
        HttpClient client, string email, string password, Guid? gymId = null)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
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

    private async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email, string password)
    {
        var client = host.CreateClient();
        var tokens = await SignInAsync(client, email, password, gymId: null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, ApiUser>();
        }));

    private async Task<World> SeedAsync(bool extraGymForOwner = false)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Seguridad Gym", $"seg-{suffix}");
        var gymB = Gym.Create("Seguridad Gym B", $"segb-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), gymB, GymFeatureFlags.Default(gymB.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Seguridad Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Seguridad Staff", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        if (extraGymForOwner)
        {
            // La misma persona, staff en otro gym: dos accesos, dos sesiones posibles.
            await provisioning.CreateGymUserAsync(ownerEmail, "Seguridad Owner", Password, UserRole.Staff, gymB.Id);
        }

        return new World(gym.Id, gymB.Id, ownerId, ownerEmail, staffId, staffEmail);
    }

    /// <summary>Un owner invitado que todavía no activó su cuenta (sin contraseña).</summary>
    private async Task<(Guid UserId, string Email)> SeedPendingOwnerAsync(Guid gymId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var email = NewEmail("pend");
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Owner Pendiente", IsActive = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        db.GymAccesses.Add(GymAccess.Grant(user.Id, gymId, UserRole.Owner));
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    private sealed record World(Guid GymA, Guid GymB, Guid OwnerId, string OwnerEmail, Guid StaffId, string StaffEmail);
}
