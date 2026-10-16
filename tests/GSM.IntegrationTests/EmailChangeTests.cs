using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Events;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ApiUser = GSM.API.Auth.CurrentUser;
using AppUser = GSM.Infrastructure.Identity.AppUser;

namespace GSM.IntegrationTests;

/// <summary>
/// I6: cambio de email con verificación (Identity §3.4) y el evento <c>UserEmailChanged</c> que sincroniza las
/// fichas de Members. Con identidad real (JWT) y el reloj de tests. Base de carril: <c>gsm_test_identidad</c>.
/// </summary>
public sealed class EmailChangeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "MemberPass_123";

    // ---- Pedir el código ----

    [Fact]
    public async Task Requesting_a_change_mails_a_sensitive_code_to_the_new_address_and_keeps_the_old_email()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = await LoggedInAsync(host, w.MemberEmail);

        var response = await RequestAsync(client, w.NewEmail);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var message = emails.Sent.Last(m => IsCodeEmailTo(m, w.NewEmail));
        Assert.True(message.Sensitive);

        // I6-1: al correo actual le llega otro código, también sensible, que nombra el correo nuevo.
        var authorization = emails.LastTo(w.MemberEmail)!;
        Assert.True(authorization.Sensitive);
        Assert.StartsWith("Autoriza el cambio", authorization.Subject);
        Assert.Contains(w.NewEmail, authorization.HtmlBody);
        Assert.NotEqual(CodeSentTo(emails, w.NewEmail), CurrentCodeSentTo(emails, w.MemberEmail));

        // El email de la cuenta no cambia hasta confirmar, y la base solo guarda el hash.
        using var scope = factory.CreateScopeWithMigration();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Equal(w.MemberEmail, (await users.FindByIdAsync(w.UserId.ToString()))!.Email);

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var row = await db.EmailChangeRequests.AsNoTracking().Where(r => r.UserId == w.UserId).SingleAsync();
        Assert.Equal(w.NewEmail, row.NewEmail);
        Assert.NotEqual(CodeSentTo(emails, w.NewEmail), row.CodeHash);
        Assert.Equal(64, row.CodeHash.Length);
        Assert.Equal(64, row.CurrentCodeHash.Length);
        Assert.NotEqual(row.CodeHash, row.CurrentCodeHash);
        Assert.Equal(row.IssuedAt + TimeSpan.FromMinutes(10), row.ExpiresAt);
    }

    [Fact]
    public async Task Requesting_the_same_email_is_a_422_and_a_malformed_email_a_400()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var client = await LoggedInAsync(host, w.MemberEmail);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await RequestAsync(client, w.MemberEmail.ToUpperInvariant())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RequestAsync(client, "no-es-un-correo")).StatusCode);
    }

    [Fact]
    public async Task An_email_that_belongs_to_another_account_is_a_409_and_no_code_is_sent()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = await LoggedInAsync(host, w.MemberEmail);

        var response = await RequestAsync(client, w.OtherAccountEmail);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(emails.LastTo(w.OtherAccountEmail));
    }

    [Fact]
    public async Task Requests_are_limited_to_one_per_minute_and_a_new_one_cancels_the_previous_code()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var clock = host.Services.GetRequiredService<MutableClock>();
        var client = await LoggedInAsync(host, w.MemberEmail);

        Assert.Equal(HttpStatusCode.Accepted, (await RequestAsync(client, w.NewEmail)).StatusCode);
        var first = CodeSentTo(emails, w.NewEmail);

        // Dentro del minuto: demasiado pronto.
        Assert.Equal(HttpStatusCode.Conflict, (await RequestAsync(client, w.NewEmail)).StatusCode);

        try
        {
            clock.PinTo(DateTimeOffset.UtcNow.AddMinutes(2), DateOnly.FromDateTime(DateTime.UtcNow));
            emails.Clear();
            Assert.Equal(HttpStatusCode.Accepted, (await RequestAsync(client, w.NewEmail)).StatusCode);
            var second = CodeSentTo(emails, w.NewEmail);

            // El primer código ya no sirve; el segundo sí. (Si salieran iguales por azar, el test no distingue.)
            if (first != second)
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmAsync(client, first, CurrentCodeSentTo(emails, w.MemberEmail))).StatusCode);
            }

            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(client, second, CurrentCodeSentTo(emails, w.MemberEmail))).StatusCode);
        }
        finally
        {
            clock.Unpin();
        }
    }

    // ---- Confirmar ----

    [Fact]
    public async Task Confirming_the_code_changes_the_login_email_and_warns_the_old_address()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = await LoggedInAsync(host, w.MemberEmail);

        await RequestAsync(client, w.NewEmail);
        var confirm = await ConfirmAsync(client, CodeSentTo(emails, w.NewEmail), CurrentCodeSentTo(emails, w.MemberEmail));

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(w.NewEmail, (await confirm.Content.ReadFromJsonAsync<EmailChangedResponse>())!.Email);

        // El email nuevo entra; el viejo ya no.
        var anonymous = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.NewEmail, Password))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.MemberEmail, Password))).StatusCode);

        // Al correo viejo le llega el aviso (sin código).
        var notice = emails.LastTo(w.MemberEmail)!;
        Assert.Contains(w.NewEmail, notice.HtmlBody);
        Assert.False(notice.Sensitive);
    }

    [Fact]
    public async Task Without_the_code_of_the_current_email_the_change_is_rejected_and_counts_as_a_miss()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = await LoggedInAsync(host, w.MemberEmail);

        await RequestAsync(client, w.NewEmail);
        var code = CodeSentTo(emails, w.NewEmail);
        var current = CurrentCodeSentTo(emails, w.MemberEmail);
        var wrongCurrent = current == "000000" ? "111111" : "000000";

        // Quien tiene la sesión y el correo nuevo, pero no el actual, no puede cambiarlo.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmAsync(client, code, wrongCurrent)).StatusCode);
        // Ni cruzando los códigos.
        if (code != current)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmAsync(client, current, code)).StatusCode);
        }

        using (var scope = factory.CreateScopeWithMigration())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.Equal(w.MemberEmail, (await users.FindByIdAsync(w.UserId.ToString()))!.Email);
        }

        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(client, code, current)).StatusCode);
    }

    [Fact]
    public async Task The_code_works_once()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = await LoggedInAsync(host, w.MemberEmail);

        await RequestAsync(client, w.NewEmail);
        var code = CodeSentTo(emails, w.NewEmail);

        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(client, code, CurrentCodeSentTo(emails, w.MemberEmail))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmAsync(client, code, CurrentCodeSentTo(emails, w.MemberEmail))).StatusCode);
    }

    [Fact]
    public async Task A_wrong_code_is_a_422_and_five_misses_exhaust_the_request_even_for_the_right_code()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = await LoggedInAsync(host, w.MemberEmail);

        await RequestAsync(client, w.NewEmail);
        var code = CodeSentTo(emails, w.NewEmail);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmAsync(client, wrong, CurrentCodeSentTo(emails, w.MemberEmail))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmAsync(client, code, CurrentCodeSentTo(emails, w.MemberEmail))).StatusCode);

        using var scope = factory.CreateScopeWithMigration();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Equal(w.MemberEmail, (await users.FindByIdAsync(w.UserId.ToString()))!.Email);
    }

    [Fact]
    public async Task The_code_expires_after_ten_minutes()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var clock = host.Services.GetRequiredService<MutableClock>();
        var client = await LoggedInAsync(host, w.MemberEmail);

        await RequestAsync(client, w.NewEmail);
        var code = CodeSentTo(emails, w.NewEmail);

        try
        {
            clock.PinTo(DateTimeOffset.UtcNow.AddMinutes(11), DateOnly.FromDateTime(DateTime.UtcNow));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmAsync(client, code, CurrentCodeSentTo(emails, w.MemberEmail))).StatusCode);
        }
        finally
        {
            clock.Unpin();
        }
    }

    [Fact]
    public async Task Confirming_without_a_pending_request_or_with_a_malformed_code_fails()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var client = await LoggedInAsync(host, w.MemberEmail);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmAsync(client, "123456", "123456")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(client, "12ab", "123456")).StatusCode);
    }

    [Fact]
    public async Task Another_account_taking_the_email_before_confirming_makes_the_confirmation_a_409()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = await LoggedInAsync(host, w.MemberEmail);

        await RequestAsync(client, w.NewEmail);
        var code = CodeSentTo(emails, w.NewEmail);

        // Mientras tanto, otra persona se queda con ese correo.
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var other = (await users.FindByIdAsync(w.OtherUserId.ToString()))!;
            Assert.True((await users.SetEmailAsync(other, w.NewEmail)).Succeeded);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(client, code, CurrentCodeSentTo(emails, w.MemberEmail))).StatusCode);
    }

    [Fact]
    public async Task Staff_and_owners_can_change_their_email_too()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = await LoggedInAsync(host, w.StaffEmail);

        Assert.Equal(HttpStatusCode.Accepted, (await RequestAsync(client, w.NewEmail)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(client, CodeSentTo(emails, w.NewEmail), CurrentCodeSentTo(emails, w.StaffEmail))).StatusCode);
    }

    [Fact]
    public async Task Both_endpoints_require_a_session()
    {
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await RequestAsync(anonymous, "x@test.local")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ConfirmAsync(anonymous, "123456", "123456")).StatusCode);
    }

    // ---- UserEmailChanged → Members ----

    [Fact]
    public async Task Confirming_updates_the_email_of_every_linked_ficha_in_every_gym_and_records_history()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var client = await LoggedInAsync(host, w.MemberEmail);

        await RequestAsync(client, w.NewEmail);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(client, CodeSentTo(emails, w.NewEmail), CurrentCodeSentTo(emails, w.MemberEmail))).StatusCode);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var fichas = await db.Set<Member>().IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == w.UserId).ToListAsync();
        Assert.Equal(2, fichas.Count);
        Assert.All(fichas, m => Assert.Equal(w.NewEmail, m.Email));

        var history = await db.Set<MemberChange>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => fichas.Select(f => f.Id).Contains(c.MemberId) && c.Field == "email").ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, c =>
        {
            Assert.Equal(w.MemberEmail, c.OldValue);
            Assert.Equal(w.NewEmail, c.NewValue);
        });

        // Las fichas de otras personas no se tocan.
        var untouched = await db.Set<Member>().IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == w.OtherFichaId);
        Assert.Equal(w.OtherAccountEmail, untouched.Email);
    }

    [Fact]
    public async Task The_ficha_sync_is_idempotent_when_the_event_is_published_twice()
    {
        var w = await SeedAsync();

        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
            var changed = new UserEmailChanged(w.UserId, w.MemberEmail, w.NewEmail);
            await publisher.PublishAsync(changed);
            await publisher.PublishAsync(changed);
        }

        using var verify = factory.CreateScopeWithMigration();
        verify.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = verify.ServiceProvider.GetRequiredService<GsmDbContext>();
        var fichas = await db.Set<Member>().IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == w.UserId).ToListAsync();
        Assert.All(fichas, m => Assert.Equal(w.NewEmail, m.Email));

        var history = await db.Set<MemberChange>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => fichas.Select(f => f.Id).Contains(c.MemberId) && c.Field == "email").ToListAsync();
        Assert.Equal(2, history.Count);
    }

    // ---- Infraestructura de los tests ----

    private static bool IsCodeEmailTo(EmailMessage m, string to)
        => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase) && m.Subject.StartsWith("Confirma tu correo nuevo");

    private static string CodeSentTo(CapturingEmailSender emails, string to)
    {
        var message = emails.Sent.LastOrDefault(m => IsCodeEmailTo(m, to))
            ?? throw new InvalidOperationException($"No se envió ningún código a {to}.");
        var match = Regex.Match(message.TextBody ?? string.Empty, @": (?<code>\d{6}) \(");
        return match.Success ? match.Groups["code"].Value : throw new InvalidOperationException("Correo sin código.");
    }

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, string newEmail)
        => client.PostAsJsonAsync("/api/me/request-email-change", new RequestEmailChangeRequest(newEmail));

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string code, string currentCode)
        => client.PostAsJsonAsync("/api/me/confirm-email-change", new ConfirmEmailChangeRequest(code, currentCode));

    private static string CurrentCodeSentTo(CapturingEmailSender emails, string to)
    {
        var message = emails.Sent.LastOrDefault(m =>
                string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase) && m.Subject.StartsWith("Autoriza el cambio"))
            ?? throw new InvalidOperationException($"No se envió ningún código de autorización a {to}.");
        var match = Regex.Match(message.TextBody ?? string.Empty, @": (?<code>\d{6}) \(");
        return match.Success ? match.Groups["code"].Value : throw new InvalidOperationException("Correo sin código.");
    }

    private async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        var tokens = body.Tokens;
        if (body.RequiresGymSelection)
        {
            // Dos gyms: se entra por el primero; el cambio de email es de la cuenta, da igual el gym.
            var select = await client.PostAsJsonAsync(
                "/api/auth/select-gym", new SelectGymRequest(body.SelectionToken!, body.Accesses!.First().AccessId));
            select.EnsureSuccessStatusCode();
            tokens = await select.Content.ReadFromJsonAsync<AuthTokensResponse>();
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
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
        var gymA = Gym.Create("Email Gym A", $"emaila-{suffix}");
        var gymB = Gym.Create("Email Gym B", $"emailb-{suffix}");
        db.AddRange(gymA, GymFeatureFlags.Default(gymA.Id), gymB, GymFeatureFlags.Default(gymB.Id));
        await db.SaveChangesAsync();

        // Una persona miembro de los dos gyms, con una ficha vinculada en cada uno.
        var memberEmail = $"member-{suffix}@test.local";
        var userId = await provisioning.CreateMemberUserAsync(memberEmail, "Email Member", Password, gymA.Id);
        await provisioning.CreateMemberUserAsync(memberEmail, "Email Member", Password, gymB.Id);

        var joined = new DateOnly(2026, 1, 1);
        var fichaA = Member.Create(gymA.Id, "Ana", "Email", $"a-{suffix}", joined, email: memberEmail);
        var fichaB = Member.Create(gymB.Id, "Ana", "Email", $"b-{suffix}", joined, email: memberEmail);
        fichaA.LinkUser(userId);
        fichaB.LinkUser(userId);

        // Otra persona, con su cuenta y su ficha: no debe verse afectada ni poder dar su email.
        var otherEmail = $"other-{suffix}@test.local";
        var otherUserId = await provisioning.CreateMemberUserAsync(otherEmail, "Otra Persona", Password, gymA.Id);
        var otherFicha = Member.Create(gymA.Id, "Otra", "Persona", $"o-{suffix}", joined, email: otherEmail);
        otherFicha.LinkUser(otherUserId);

        db.AddRange(fichaA, fichaB, otherFicha);
        await db.SaveChangesAsync();

        var staffEmail = $"staff-{suffix}@test.local";
        await provisioning.CreateGymUserAsync(staffEmail, "Email Staff", Password, UserRole.Staff, gymA.Id);

        return new World(userId, memberEmail, $"nuevo-{suffix}@test.local", otherEmail, otherUserId, otherFicha.Id, staffEmail);
    }

    private sealed record World(
        Guid UserId, string MemberEmail, string NewEmail, string OtherAccountEmail, Guid OtherUserId,
        Guid OtherFichaId, string StaffEmail);
}
