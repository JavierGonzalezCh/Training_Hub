using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Logs;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ApiUser = GSM.API.Auth.CurrentUser;

namespace GSM.IntegrationTests;

/// <summary>
/// I5: login del miembro con código por correo (Identity §6) y marca pública del gym. Con identidad real (JWT)
/// y el reloj de tests para los vencimientos y los límites. Base de carril: <c>gsm_test_identidad</c>.
/// </summary>
public sealed class MemberLoginCodeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "MemberPass_123";

    // ---- Camino feliz ----

    [Fact]
    public async Task The_emailed_code_signs_the_member_in_and_works_only_once()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        var request = await RequestCodeAsync(client, w.MemberEmail);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var code = CodeSentTo(emails, w.MemberEmail);

        var login = await VerifyAsync(client, w.MemberEmail, code);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(body.RequiresGymSelection);
        Assert.False(string.IsNullOrEmpty(body.Tokens!.AccessToken));
        Assert.False(string.IsNullOrEmpty(body.Tokens.RefreshToken));

        // La sesión es de miembro en ese gym.
        var me = await GetMeAsync(host, body.Tokens.AccessToken);
        Assert.Equal("member", me.GetProperty("role").GetString());
        Assert.Equal(w.GymA, me.GetProperty("gymId").GetGuid());

        // Un solo uso.
        var again = await VerifyAsync(client, w.MemberEmail, code);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task The_code_email_is_marked_sensitive_and_the_database_keeps_only_a_hash()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        await RequestCodeAsync(client, w.MemberEmail, w.SlugA);
        var message = emails.LastTo(w.MemberEmail)!;
        var code = CodeSentTo(emails, w.MemberEmail);

        Assert.True(message.Sensitive);
        // Nombra el gym cuando el cliente lo indica.
        Assert.Contains("Login Gym A", message.Subject);

        using var scope = factory.CreateScopeWithMigration();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var row = await db.LoginCodes.AsNoTracking().Where(c => c.UserId == w.MemberUserId)
            .OrderByDescending(c => c.IssuedAt).FirstAsync();
        Assert.NotEqual(code, row.CodeHash);
        Assert.Equal(64, row.CodeHash.Length);
        Assert.Equal(row.IssuedAt + TimeSpan.FromMinutes(10), row.ExpiresAt);
    }

    // ---- Sin revelar cuentas ----

    [Fact]
    public async Task Requesting_a_code_answers_the_same_for_unknown_staff_only_and_member_accounts()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        var unknown = await RequestCodeAsync(client, "nadie@nowhere.test");
        var staffOnly = await RequestCodeAsync(client, w.StaffEmail);
        var member = await RequestCodeAsync(client, w.MemberEmail);

        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, staffOnly.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, member.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await member.Content.ReadAsStringAsync());

        // Solo la cuenta de miembro recibe el correo.
        Assert.Null(emails.LastTo("nadie@nowhere.test"));
        Assert.Null(emails.LastTo(w.StaffEmail));
        Assert.NotNull(emails.LastTo(w.MemberEmail));

        // Y verificar con un email ajeno o de solo equipo falla igual que con un código malo.
        var badForUnknown = await VerifyAsync(client, "nadie@nowhere.test", "123456");
        var badForStaff = await VerifyAsync(client, w.StaffEmail, "123456");
        Assert.Equal(HttpStatusCode.Unauthorized, badForUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, badForStaff.StatusCode);
        Assert.Equal(await DetailAsync(badForUnknown), await DetailAsync(badForStaff));
    }

    [Fact]
    public async Task A_malformed_code_is_a_400_and_never_reaches_the_service()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, w.MemberEmail, "12345")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, w.MemberEmail, "abcdef")).StatusCode);
    }

    // ---- Vencimiento, intentos y un código a la vez ----

    [Fact]
    public async Task The_code_expires_after_ten_minutes()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var clock = host.Services.GetRequiredService<MutableClock>();
        var client = host.CreateClient();

        try
        {
            var start = DateTimeOffset.UtcNow;
            clock.PinTo(start, DateOnly.FromDateTime(DateTime.UtcNow));
            await RequestCodeAsync(client, w.MemberEmail);
            var code = CodeSentTo(emails, w.MemberEmail);

            // A los 11 minutos el código ya no vale; uno nuevo, pedido a los 12, sirve si se usa dentro de sus 10 minutos.
            clock.PinTo(start.AddMinutes(11), DateOnly.FromDateTime(DateTime.UtcNow));
            Assert.Equal(HttpStatusCode.Unauthorized, (await VerifyAsync(client, w.MemberEmail, code)).StatusCode);

            clock.PinTo(start.AddMinutes(12), DateOnly.FromDateTime(DateTime.UtcNow));
            await RequestCodeAsync(client, w.MemberEmail);
            var fresh = CodeSentTo(emails, w.MemberEmail);
            clock.PinTo(start.AddMinutes(19), DateOnly.FromDateTime(DateTime.UtcNow));
            Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(client, w.MemberEmail, fresh)).StatusCode);
        }
        finally
        {
            clock.Unpin();
        }
    }

    [Fact]
    public async Task Five_wrong_attempts_exhaust_the_code_even_for_the_right_one()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        await RequestCodeAsync(client, w.MemberEmail);
        var code = CodeSentTo(emails, w.MemberEmail);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await VerifyAsync(client, w.MemberEmail, wrong)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await VerifyAsync(client, w.MemberEmail, code)).StatusCode);
    }

    [Fact]
    public async Task The_fifth_wrong_attempt_leaves_a_code_attempts_exceeded_security_event_without_the_code()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        await RequestCodeAsync(client, w.MemberEmail);
        var code = CodeSentTo(emails, w.MemberEmail);
        var wrong = code == "000000" ? "111111" : "000000";

        HttpResponseMessage fifth = null!;
        for (var i = 0; i < 5; i++)
        {
            fifth = await VerifyAsync(client, w.MemberEmail, wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, fifth.StatusCode);
        }

        var traceId = fifth.Headers.GetValues("X-Trace-Id").Single();
        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var logs = host.Services.GetRequiredService<IDbContextFactory<LogsDbContext>>().CreateDbContext();

        // Un solo evento, en el intento que agotó el código; los cuatro anteriores no lo generan.
        var row = await logs.SecurityEvents.SingleAsync(e => e.EventType == "code_attempts_exceeded" && e.Detail!.Contains(w.MemberEmail));
        Assert.Equal(traceId, row.TraceId);
        Assert.Equal($"email={w.MemberEmail}; intentos=5", row.Detail);
    }

    [Fact]
    public async Task Four_wrong_attempts_still_leave_the_right_code_valid()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        await RequestCodeAsync(client, w.MemberEmail);
        var code = CodeSentTo(emails, w.MemberEmail);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 4; i++)
        {
            await VerifyAsync(client, w.MemberEmail, wrong);
        }

        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(client, w.MemberEmail, code)).StatusCode);
    }

    [Fact]
    public async Task A_new_code_cancels_the_previous_one()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var clock = host.Services.GetRequiredService<MutableClock>();
        var client = host.CreateClient();

        try
        {
            var start = DateTimeOffset.UtcNow;
            clock.PinTo(start, DateOnly.FromDateTime(DateTime.UtcNow));
            await RequestCodeAsync(client, w.MemberEmail);
            var first = CodeSentTo(emails, w.MemberEmail);

            clock.PinTo(start.AddSeconds(90), DateOnly.FromDateTime(DateTime.UtcNow));
            await RequestCodeAsync(client, w.MemberEmail);
            var second = CodeSentTo(emails, w.MemberEmail);

            if (first != second)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await VerifyAsync(client, w.MemberEmail, first)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(client, w.MemberEmail, second)).StatusCode);
        }
        finally
        {
            clock.Unpin();
        }
    }

    // ---- Límites de petición: uno por minuto, cinco por hora ----

    [Fact]
    public async Task Only_one_code_per_minute_is_sent()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var clock = host.Services.GetRequiredService<MutableClock>();
        var client = host.CreateClient();

        try
        {
            var start = DateTimeOffset.UtcNow;
            clock.PinTo(start, DateOnly.FromDateTime(DateTime.UtcNow));
            await RequestCodeAsync(client, w.MemberEmail);
            clock.PinTo(start.AddSeconds(30), DateOnly.FromDateTime(DateTime.UtcNow));
            var throttled = await RequestCodeAsync(client, w.MemberEmail);

            // I5-7: una cuenta real que supera el límite recibe 409 con el aviso, y no sale un segundo correo.
            Assert.Equal(HttpStatusCode.Conflict, throttled.StatusCode);
            Assert.Contains("Pediste demasiados códigos. Espera unos minutos e inténtalo de nuevo.", await throttled.Content.ReadAsStringAsync());
            Assert.Single(emails.Sent, m => LoginCodeEmailTo(m, w.MemberEmail));

            clock.PinTo(start.AddSeconds(61), DateOnly.FromDateTime(DateTime.UtcNow));
            await RequestCodeAsync(client, w.MemberEmail);
            Assert.Equal(2, emails.Sent.Count(m => LoginCodeEmailTo(m, w.MemberEmail)));
        }
        finally
        {
            clock.Unpin();
        }
    }

    [Fact]
    public async Task At_most_five_codes_per_hour_are_issued()
    {
        var w = await SeedAsync();

        // Directo al servicio: el límite por IP del endpoint (10/min) no deja pedir tantos por HTTP con holgura.
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var service = scope.ServiceProvider.GetRequiredService<ILoginCodeService>();

        try
        {
            var start = DateTimeOffset.UtcNow;
            for (var i = 0; i < 5; i++)
            {
                clock.PinTo(start.AddMinutes(i * 2), DateOnly.FromDateTime(DateTime.UtcNow));
                Assert.Equal(LoginCodeIssueStatus.Issued, (await service.IssueAsync(w.MemberEmail)).Status);
            }

            // Pasado el minuto pero dentro de la hora: el sexto se frena.
            clock.PinTo(start.AddMinutes(11), DateOnly.FromDateTime(DateTime.UtcNow));
            Assert.Equal(LoginCodeIssueStatus.Throttled, (await service.IssueAsync(w.MemberEmail)).Status);

            // Una hora después de los primeros, vuelve a poder.
            clock.PinTo(start.AddMinutes(70), DateOnly.FromDateTime(DateTime.UtcNow));
            Assert.Equal(LoginCodeIssueStatus.Issued, (await service.IssueAsync(w.MemberEmail)).Status);
        }
        finally
        {
            clock.Unpin();
        }
    }

    // ---- Bloqueo por contraseñas (§7) y accesos de equipo ----

    [Fact]
    public async Task The_code_works_while_the_account_is_locked_by_wrong_passwords()
    {
        var w = await SeedAsync();

        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var credentials = scope.ServiceProvider.GetRequiredService<IUserCredentialsService>();
            for (var i = 0; i < 5; i++)
            {
                await credentials.SignInAsync(w.MemberEmail, "Wrong_Pass_999");
            }

            // Bloqueada: ni la contraseña buena entra.
            Assert.Null((await credentials.SignInAsync(w.MemberEmail, Password)).Account);
        }

        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        await RequestCodeAsync(client, w.MemberEmail);
        var login = await VerifyAsync(client, w.MemberEmail, CodeSentTo(emails, w.MemberEmail));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task The_code_only_opens_member_accesses_not_the_team_access_of_the_same_person()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        // La persona es staff en el gym A y también miembro del gym B.
        await RequestCodeAsync(client, w.BothEmail);
        var login = await VerifyAsync(client, w.BothEmail, CodeSentTo(emails, w.BothEmail));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(body.RequiresGymSelection);
        var me = await GetMeAsync(host, body.Tokens!.AccessToken);
        Assert.Equal("member", me.GetProperty("role").GetString());
        Assert.Equal(w.GymB, me.GetProperty("gymId").GetGuid());
    }

    [Fact]
    public async Task A_member_of_two_gyms_gets_the_gym_picker_after_the_code()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        await RequestCodeAsync(client, w.TwoGymsEmail);
        var login = await VerifyAsync(client, w.TwoGymsEmail, CodeSentTo(emails, w.TwoGymsEmail));

        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.True(body.RequiresGymSelection);
        Assert.Equal(2, body.Accesses!.Count);
        Assert.All(body.Accesses, a => Assert.Equal("member", a.Role));

        var pick = body.Accesses.Single(a => a.GymId == w.GymB);
        var select = await client.PostAsJsonAsync("/api/auth/select-gym", new SelectGymRequest(body.SelectionToken!, pick.AccessId));
        Assert.Equal(HttpStatusCode.OK, select.StatusCode);
    }

    [Fact]
    public async Task A_deactivated_account_gets_no_code()
    {
        var w = await SeedAsync();
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            await db.Users.Where(u => u.Id == w.MemberUserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.Accepted, (await RequestCodeAsync(client, w.MemberEmail)).StatusCode);
        Assert.Null(emails.LastTo(w.MemberEmail));
    }

    // ---- Sesión persistente (§6) ----

    [Fact]
    public async Task Every_renewal_extends_the_refresh_token_another_ninety_days()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = host.CreateClient();

        await RequestCodeAsync(client, w.MemberEmail);
        var login = await VerifyAsync(client, w.MemberEmail, CodeSentTo(emails, w.MemberEmail));
        var first = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Tokens!;

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = (await refreshed.Content.ReadFromJsonAsync<AuthTokensResponse>())!;

        // El token usado ya no sirve; el nuevo sí, y vence 90 días después de la renovación.
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken))).StatusCode);

        using var scope = factory.CreateScopeWithMigration();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var newest = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == w.MemberUserId && t.RevokedAt == null)
            .OrderByDescending(t => t.ExpiresAt).FirstAsync();
        Assert.True(newest.ExpiresAt > DateTimeOffset.UtcNow.AddDays(89));
        Assert.True(newest.ExpiresAt <= DateTimeOffset.UtcNow.AddDays(90).AddMinutes(1));
    }

    // ---- Marca pública del gym ----

    [Fact]
    public async Task The_public_branding_of_a_gym_is_served_by_slug_without_a_session()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var client = host.CreateClient();

        var ok = await client.GetAsync($"/api/public/gyms/{w.SlugA}/branding");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var branding = (await ok.Content.ReadFromJsonAsync<PublicGymBrandingResponse>())!;
        Assert.Equal("Login Gym A", branding.Name);
        Assert.False(string.IsNullOrEmpty(branding.PrimaryColor));
        Assert.False(string.IsNullOrEmpty(branding.SecondaryColor));

        // El slug no distingue mayúsculas.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/public/gyms/{w.SlugA.ToUpperInvariant()}/branding")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/public/gyms/no-existe-xyz/branding")).StatusCode);
    }

    // ---- Infraestructura de los tests ----

    private static bool LoginCodeEmailTo(EmailMessage m, string to)
        => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase) && m.Subject.StartsWith("Tu código para entrar");

    private static string CodeSentTo(CapturingEmailSender emails, string to)
    {
        var message = emails.Sent.LastOrDefault(m => LoginCodeEmailTo(m, to))
            ?? throw new InvalidOperationException($"No se envió ningún código a {to}.");
        var match = Regex.Match(message.TextBody ?? string.Empty, @": (?<code>\d{6}) \(");
        return match.Success ? match.Groups["code"].Value : throw new InvalidOperationException(message.TextBody);
    }

    private static Task<HttpResponseMessage> RequestCodeAsync(HttpClient client, string email, string? gymSlug = null)
        => client.PostAsJsonAsync("/api/auth/request-login-code", new RequestLoginCodeRequest(email, gymSlug));

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, string email, string code)
        => client.PostAsJsonAsync("/api/auth/verify-login-code", new VerifyLoginCodeRequest(email, code));

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }

    private static async Task<JsonElement> GetMeAsync(WebApplicationFactory<Program> host, string accessToken)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, ApiUser>();
        }));

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slugA = $"logina-{suffix}";
        var gymA = Gym.Create("Login Gym A", slugA);
        var gymB = Gym.Create("Login Gym B", $"loginb-{suffix}");
        db.AddRange(gymA, GymFeatureFlags.Default(gymA.Id), gymB, GymFeatureFlags.Default(gymB.Id));
        await db.SaveChangesAsync();

        // Un miembro de un solo gym.
        var memberEmail = $"member-{suffix}@test.local";
        var memberUserId = await provisioning.CreateMemberUserAsync(memberEmail, "Login Member", Password, gymA.Id);

        // Staff sin acceso de miembro.
        var staffEmail = $"staff-{suffix}@test.local";
        await provisioning.CreateGymUserAsync(staffEmail, "Login Staff", Password, UserRole.Staff, gymA.Id);

        // Staff en el gym A y miembro en el B: la misma persona.
        var bothEmail = $"both-{suffix}@test.local";
        await provisioning.CreateGymUserAsync(bothEmail, "Login Both", Password, UserRole.Staff, gymA.Id);
        await provisioning.CreateMemberUserAsync(bothEmail, "Login Both", Password, gymB.Id);

        // Miembro de los dos gyms.
        var twoEmail = $"two-{suffix}@test.local";
        await provisioning.CreateMemberUserAsync(twoEmail, "Login Two", Password, gymA.Id);
        await provisioning.CreateMemberUserAsync(twoEmail, "Login Two", Password, gymB.Id);

        return new World(gymA.Id, gymB.Id, slugA, memberUserId, memberEmail, staffEmail, bothEmail, twoEmail);
    }

    private sealed record World(
        Guid GymA, Guid GymB, string SlugA, Guid MemberUserId,
        string MemberEmail, string StaffEmail, string BothEmail, string TwoGymsEmail);
}
