using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
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
/// FL1: el miembro entra con su correo o su celular (10 dígitos, sin +57); la primera vez, sin cuenta, su clave
/// provisional es el documento de su ficha. Entra obligado a crear su contraseña: hasta entonces la API solo deja
/// crearla, ver la sesión y salir. Después, el documento ya no sirve.
/// </summary>
public sealed class FirstLoginTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string NewPassword = "Mi_Clave_2026";

    [Fact]
    public async Task First_login_with_the_document_creates_the_account_and_forces_a_new_password()
    {
        var w = await SeedGymAsync();
        var m = await SeedMemberAsync(w, document: "1.020.304.050");
        await using var host = RealIdentityHost();
        var client = host.CreateClient();

        var tokens = await LoginAsync(client, m.Email, "1020304050");
        Assert.True(MustChange(tokens.AccessToken));

        Authorize(client, tokens);
        var blocked = await client.GetAsync("/api/me/streak");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("password_change_required", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var weak = await client.PostAsJsonAsync("/api/me/initial-password", new SetInitialPasswordRequest("solominusculas"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, weak.StatusCode);

        var created = await client.PostAsJsonAsync("/api/me/initial-password", new SetInitialPasswordRequest(NewPassword));
        created.EnsureSuccessStatusCode();
        var fresh = (await created.Content.ReadFromJsonAsync<AuthTokensResponse>())!;
        Assert.False(MustChange(fresh.AccessToken));

        Authorize(client, fresh);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/streak")).StatusCode);

        var anonymous = host.CreateClient();
        Assert.False(MustChange((await LoginAsync(anonymous, m.Email, NewPassword)).AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(anonymous, m.Email, "1020304050"));

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var member = await db.Set<Member>().IgnoreQueryFilters().SingleAsync(x => x.Id == m.MemberId);
        Assert.NotNull(member.UserId);
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("+57{0}")]
    [InlineData("57 {1} {2} {3}")]
    public async Task The_phone_without_the_country_code_works_and_so_do_other_spellings(string format)
    {
        var w = await SeedGymAsync();
        var m = await SeedMemberAsync(w, document: "52987654");
        await using var host = RealIdentityHost();

        var typed = string.Format(format, m.Phone10, m.Phone10[..3], m.Phone10[3..6], m.Phone10[6..]);
        var tokens = await LoginAsync(host.CreateClient(), typed, "52987654");
        Assert.True(MustChange(tokens.AccessToken));
    }

    [Fact]
    public async Task Before_creating_the_password_the_document_still_opens_the_account()
    {
        var w = await SeedGymAsync();
        var m = await SeedMemberAsync(w, document: "80111222");
        await using var host = RealIdentityHost();

        await LoginAsync(host.CreateClient(), m.Email, "80111222");
        var again = await LoginAsync(host.CreateClient(), m.Phone10, "80111222");

        Assert.True(MustChange(again.AccessToken));
    }

    [Fact]
    public async Task A_wrong_document_an_inactive_member_or_a_foreign_number_do_not_get_in()
    {
        var w = await SeedGymAsync();
        var m = await SeedMemberAsync(w, document: "70333444");
        var inactive = await SeedMemberAsync(w, document: "70555666", inactive: true);
        await using var host = RealIdentityHost();
        var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(client, m.Email, "70333445"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(client, inactive.Email, "70555666"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(client, "+14155550100", "70333444"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(client, "3000000000", "70333444"));
    }

    [Fact]
    public async Task A_phone_shared_by_two_people_asks_for_the_email()
    {
        var w = await SeedGymAsync();
        var first = await SeedMemberAsync(w, document: "60111111");
        await SeedMemberAsync(w, document: "60222222", phone10: first.Phone10);
        await using var host = RealIdentityHost();

        var login = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(first.Phone10, "60111111"));

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("Entra con tu correo", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_member_who_already_has_an_account_keeps_logging_in_with_their_password()
    {
        var w = await SeedGymAsync();
        var m = await SeedMemberAsync(w, document: "90123456", withAccountPassword: "Ya_Tengo_Clave1");
        await using var host = RealIdentityHost();
        var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(client, m.Email, "90123456"));
        Assert.False(MustChange((await LoginAsync(client, m.Phone10, "Ya_Tengo_Clave1")).AccessToken));
    }

    [Fact]
    public async Task The_same_email_in_two_gyms_gets_access_to_both()
    {
        var w1 = await SeedGymAsync();
        var w2 = await SeedGymAsync();
        var m = await SeedMemberAsync(w1, document: "40999888");
        await SeedMemberAsync(w2, document: "40999888", email: m.Email);
        await using var host = RealIdentityHost();

        var login = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(m.Email, "40999888"));
        login.EnsureSuccessStatusCode();
        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;

        Assert.True(body.RequiresGymSelection);
        Assert.Equal(2, body.Accesses!.Count);
    }

    private static bool MustChange(string accessToken)
        => new JsonWebToken(accessToken).TryGetClaim("pwd_change", out var claim) && claim.Value == "true";

    private static void Authorize(HttpClient client, AuthTokensResponse tokens)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

    private static async Task<AuthTokensResponse> LoginAsync(HttpClient client, string identifier, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(identifier, password));
        Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Tokens!;
    }

    private static async Task<HttpStatusCode> LoginStatusAsync(HttpClient client, string identifier, string password)
        => (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(identifier, password))).StatusCode;

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, ApiUser>();
        }));

    private async Task<Guid> SeedGymAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Primer Ingreso", $"fl-{Guid.NewGuid():N}"[..12]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();
        return gym.Id;
    }

    private async Task<SeededMember> SeedMemberAsync(
        Guid gymId, string document, string? phone10 = null, string? email = null,
        bool inactive = false, string? withAccountPassword = null)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // Celular único por prueba: la base se comparte entre ejecuciones.
        phone10 ??= "3" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        email ??= $"fl-{Guid.NewGuid():N}"[..14] + "@test.local";

        var member = Member.Create(gymId, "Primer", "Ingreso", document, clock.Today, phone: phone10, email: email);
        db.Add(member);
        await db.SaveChangesAsync();

        if (withAccountPassword is not null)
        {
            var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
            member.LinkUser(await provisioning.CreateMemberUserAsync(email, "Primer Ingreso", withAccountPassword, gymId));
        }

        if (inactive)
        {
            db.Entry(member).Property(nameof(Member.Status)).CurrentValue = MemberStatus.Inactive;
        }

        await db.SaveChangesAsync();
        return new SeededMember(member.Id, email, phone10);
    }

    private sealed record SeededMember(Guid MemberId, string Email, string Phone10);
}
