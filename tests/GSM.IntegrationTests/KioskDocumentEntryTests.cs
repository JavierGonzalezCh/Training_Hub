using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.API.Auth;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Logs;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// A4 — entrada con documento en el kiosco, de punta a punta con identidad real (cuenta <c>kiosk</c>, patrón de
/// <see cref="KioskDeviceTests"/>).
/// </summary>
public sealed class KioskDocumentEntryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private const string Path = "/api/attendance/kiosk/document-entry";

    [Fact]
    public async Task A_valid_document_with_a_current_membership_enters_and_the_screen_gets_only_first_name_and_photo()
    {
        var w = await SeedAsync(documentOn: true);
        await using var host = RealIdentityHost();
        var kiosk = await PairedKioskAsync(host, w.OwnerEmail);

        var response = await kiosk.PostAsJsonAsync(Path, new { document = w.Document });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"firstName\":\"Ana\"", body);
        Assert.DoesNotContain("Prueba", body);          // sin apellido
        Assert.DoesNotContain(w.Document, body);
        Assert.Contains("\"expiringSoon\":false", body);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var saved = await db.Set<GSM.Domain.Attendance.Attendance>().SingleAsync(a => a.MemberId == w.MemberId);
        Assert.Equal(GSM.Domain.Attendance.AttendanceMethod.Document, saved.Method);

        // Una entrada por día: la segunda es el mismo rechazo genérico.
        var again = await kiosk.PostAsJsonAsync(Path, new { document = w.Document });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains(KioskDocumentEntryHandler.GenericRejection, await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_other_gym_and_blocked_documents_get_the_same_rejection_without_revealing_anything()
    {
        var w = await SeedAsync(documentOn: true);
        var other = await SeedAsync(documentOn: true);
        var expired = await AddMemberAsync(w.GymId, "Beto", membershipEndedDaysAgo: 40);
        await using var host = RealIdentityHost();
        var kiosk = await PairedKioskAsync(host, w.OwnerEmail);

        var unknown = await kiosk.PostAsJsonAsync(Path, new { document = "99999999" });
        var ofOtherGym = await kiosk.PostAsJsonAsync(Path, new { document = other.Document });
        var blocked = await kiosk.PostAsJsonAsync(Path, new { document = expired });

        foreach (var r in new[] { unknown, ofOtherGym, blocked })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var text = await r.Content.ReadAsStringAsync();
            Assert.Contains(KioskDocumentEntryHandler.GenericRejection, text);
            Assert.DoesNotContain("venci", text);
            Assert.DoesNotContain("membres", text);
        }

        // El miembro de otro gym no entró, y el bloqueado dejó su intento para la campana.
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.False(await db.Set<GSM.Domain.Attendance.Attendance>().AnyAsync(a => a.MemberId == other.MemberId));
        Assert.True(await db.Set<GSM.Domain.Attendance.CheckinAttempt>().AnyAsync(a => a.GymId == w.GymId));
    }

    [Fact]
    public async Task Five_failures_lock_the_keypad_and_leave_a_security_event_even_for_a_valid_document()
    {
        var w = await SeedAsync(documentOn: true);
        await using var host = RealIdentityHost();
        var kiosk = await PairedKioskAsync(host, w.OwnerEmail);
        var kioskId = new JwtSecurityTokenHandler()
            .ReadJwtToken(kiosk.DefaultRequestHeaders.Authorization!.Parameter).Subject;

        for (var i = 0; i < 5; i++)
        {
            var miss = await kiosk.PostAsJsonAsync(Path, new { document = $"8888000{i}" });
            Assert.Equal(HttpStatusCode.Conflict, miss.StatusCode);
        }

        var locked = await kiosk.PostAsJsonAsync(Path, new { document = w.Document });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.True(locked.Headers.RetryAfter is not null);

        await host.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using var logs = host.Services.GetRequiredService<IDbContextFactory<LogsDbContext>>().CreateDbContext();
        var row = await logs.SecurityEvents.SingleAsync(e =>
            e.EventType == "document_entry_locked" && e.Detail!.Contains(kioskId));
        Assert.DoesNotContain(w.Document, row.Detail);
    }

    [Fact]
    public async Task A_success_resets_the_failure_count()
    {
        var w = await SeedAsync(documentOn: true);
        await using var host = RealIdentityHost();
        var kiosk = await PairedKioskAsync(host, w.OwnerEmail);

        for (var i = 0; i < 4; i++)
        {
            await kiosk.PostAsJsonAsync(Path, new { document = $"7777000{i}" });
        }

        Assert.Equal(HttpStatusCode.Created, (await kiosk.PostAsJsonAsync(Path, new { document = w.Document })).StatusCode);

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Conflict, (await kiosk.PostAsJsonAsync(Path, new { document = $"6666000{i}" })).StatusCode);
        }
    }

    [Fact]
    public async Task Staff_and_owner_get_403_and_a_gym_with_the_flag_off_rejects_the_kiosk()
    {
        var w = await SeedAsync(documentOn: true);
        await using var host = RealIdentityHost();

        foreach (var email in new[] { w.StaffEmail, w.OwnerEmail })
        {
            var person = await LoggedInAsync(host, email);
            Assert.Equal(HttpStatusCode.Forbidden, (await person.PostAsJsonAsync(Path, new { document = w.Document })).StatusCode);
        }

        var off = await SeedAsync(documentOn: false);
        var kiosk = await PairedKioskAsync(host, off.OwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await kiosk.PostAsJsonAsync(Path, new { document = off.Document })).StatusCode);

        // Y el kiosco con el flag encendido sabe que debe mostrar el teclado.
        var onKiosk = await PairedKioskAsync(host, w.OwnerEmail);
        Assert.Contains("\"byDocument\":true", await onKiosk.GetStringAsync("/api/attendance/kiosk-code"));
    }

    [Fact]
    public async Task A_malformed_document_is_a_form_error()
    {
        var w = await SeedAsync(documentOn: true);
        await using var host = RealIdentityHost();
        var kiosk = await PairedKioskAsync(host, w.OwnerEmail);

        Assert.Equal(HttpStatusCode.BadRequest, (await kiosk.PostAsJsonAsync(Path, new { document = "12ab" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await kiosk.PostAsJsonAsync(Path, new { document = "" })).StatusCode);
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.Tokens!.AccessToken);
        return client;
    }

    private static async Task<HttpClient> PairedKioskAsync(WebApplicationFactory<Program> host, string ownerEmail)
    {
        var owner = await LoggedInAsync(host, ownerEmail);
        var created = await owner.PostAsJsonAsync("/api/gym/kiosks", new { name = "Entrada" });
        created.EnsureSuccessStatusCode();
        var pairing = (await created.Content.ReadFromJsonAsync<KioskCreatedResponse>())!;

        var client = host.CreateClient();
        var tokens = (await (await client.PostAsJsonAsync("/api/auth/kiosk/pair", new { pairingCode = pairing.PairingCode }))
            .Content.ReadFromJsonAsync<AuthTokensResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private async Task<string> AddMemberAsync(Guid gymId, string firstName, int membershipEndedDaysAgo)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IClock>().Today;

        var document = NewDocument();
        var member = Member.Create(gymId, firstName, "Vencido", document, today.AddDays(-9000));
        var plan = MembershipPlan.Create(gymId, "Mensual", null, 50000m, 30);
        var end = today.AddDays(-membershipEndedDaysAgo);
        db.AddRange(member, plan, Membership.Create(gymId, member.Id, plan.Id, end.AddDays(-29), 30));
        await db.SaveChangesAsync();
        return document;
    }

    private static string NewDocument() => Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();

    private async Task<World> SeedAsync(bool documentOn)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IClock>().Today;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Doc Gym", $"doc-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));

        var document = NewDocument();
        var member = Member.Create(gym.Id, "Ana", "Prueba", document, today.AddDays(-9000));
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.AddRange(member, plan, Membership.Create(gym.Id, member.Id, plan.Id, today.AddDays(-5), 30));

        if (documentOn)
        {
            var settings = GSM.Domain.Attendance.CheckinSettings.Default(gym.Id);
            settings.Update(GSM.Domain.Attendance.CheckinMode.Rotating, true);
            db.Add(settings);
        }

        await db.SaveChangesAsync();

        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Doc Owner", Password, UserRole.Owner, gym.Id);
        await provisioning.CreateGymUserAsync(staffEmail, "Doc Staff", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(gym.Id, member.Id, document, ownerEmail, staffEmail);
    }

    private sealed record World(Guid GymId, Guid MemberId, string Document, string OwnerEmail, string StaffEmail);
}
