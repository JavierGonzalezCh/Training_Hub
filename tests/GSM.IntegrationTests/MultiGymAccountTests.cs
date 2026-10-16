using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// I2: una cuenta con accesos a varios gyms. Identidad real (JWT) de punta a punta: login, selección,
/// cambio de gym, desactivación por gym, invitaciones que reconocen cuentas existentes.
/// </summary>
public sealed class MultiGymAccountTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "SharedPass_123";

    [Fact]
    public async Task Single_access_enters_directly_and_the_token_carries_gym_and_role()
    {
        var w = await SeedAsync(sharedPerson: false);
        await using var host = RealIdentityHost();

        var login = await LoginAsync(host.CreateClient(), w.PersonEmail);

        Assert.False(login.RequiresGymSelection);
        var claims = Claims(login.Tokens!.AccessToken);
        Assert.Equal(w.GymA.ToString(), claims.GetProperty("gym_id").GetString());
        Assert.Equal("owner", claims.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Two_gyms_return_the_list_last_used_first_and_the_chosen_token_sees_only_that_gym()
    {
        var w = await SeedAsync(sharedPerson: true);
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();

        var first = await LoginAsync(anonymous, w.PersonEmail);
        Assert.True(first.RequiresGymSelection);
        Assert.Null(first.Tokens);
        Assert.Equal(2, first.Accesses!.Count);
        Assert.Contains(first.Accesses, a => a.GymId == w.GymA && a.Role == "owner" && a.GymName == "Gym A");
        Assert.Contains(first.Accesses, a => a.GymId == w.GymB && a.Role == "staff");

        // Elige B: su token es de B, ve el miembro de B y no el de A.
        var pickB = first.Accesses.Single(a => a.GymId == w.GymB);
        var tokensB = await SelectAsync(anonymous, first.SelectionToken!, pickB.AccessId);
        var claims = Claims(tokensB.AccessToken);
        Assert.Equal(w.GymB.ToString(), claims.GetProperty("gym_id").GetString());
        Assert.Equal("staff", claims.GetProperty("role").GetString());

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokensB.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/members/{w.MemberB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/members/{w.MemberA}")).StatusCode);

        // El último usado queda primero en el siguiente login.
        var second = await LoginAsync(anonymous, w.PersonEmail);
        Assert.Equal(w.GymB, second.Accesses![0].GymId);
    }

    [Fact]
    public async Task Gym_selection_ticket_is_not_an_access_token()
    {
        var w = await SeedAsync(sharedPerson: true);
        await using var host = RealIdentityHost();
        var login = await LoginAsync(host.CreateClient(), w.PersonEmail);

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.SelectionToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        var bogus = await host.CreateClient().PostAsJsonAsync(
            "/api/auth/select-gym", new SelectGymRequest("no-es-un-token", login.Accesses![0].AccessId));
        Assert.Equal(HttpStatusCode.Unauthorized, bogus.StatusCode);
    }

    [Fact]
    public async Task Switch_gym_issues_the_other_gym_without_password_and_rejects_inactive_or_foreign_accesses()
    {
        var w = await SeedAsync(sharedPerson: true);
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();
        var login = await LoginAsync(anonymous, w.PersonEmail);
        var accessA = login.Accesses!.Single(a => a.GymId == w.GymA);
        var accessB = login.Accesses!.Single(a => a.GymId == w.GymB);
        var tokensA = await SelectAsync(anonymous, login.SelectionToken!, accessA.AccessId);

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokensA.AccessToken);

        // El menú lista ambos y marca el actual.
        var mine = await client.GetFromJsonAsync<List<GymAccessOption>>("/api/auth/accesses");
        Assert.Equal(2, mine!.Count);
        Assert.True(mine.Single(a => a.GymId == w.GymA).IsCurrent);
        Assert.False(mine.Single(a => a.GymId == w.GymB).IsCurrent);

        var switched = await client.PostAsJsonAsync(
            "/api/auth/switch-gym", new SwitchGymRequest(accessB.AccessId, tokensA.RefreshToken));
        switched.EnsureSuccessStatusCode();
        var tokensB = (await switched.Content.ReadFromJsonAsync<AuthTokensResponse>())!;
        Assert.Equal(w.GymB.ToString(), Claims(tokensB.AccessToken).GetProperty("gym_id").GetString());

        // La sesión que se dejó ya no renueva.
        var oldRefresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokensA.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);

        // Acceso ajeno: 403.
        var other = await SeedAsync(sharedPerson: false);
        var foreign = await LoginAsync(host.CreateClient(), other.PersonEmail);
        var foreignAccess = foreign.Tokens!.AccessToken; // un solo gym: el id del acceso sale de /accesses
        var foreignClient = host.CreateClient();
        foreignClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignAccess);
        var foreignId = (await foreignClient.GetFromJsonAsync<List<GymAccessOption>>("/api/auth/accesses"))!.Single().AccessId;
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/auth/switch-gym", new SwitchGymRequest(foreignId))).StatusCode);

        // Acceso inactivo: 403.
        await SetAccessActiveAsync(accessA.AccessId, false);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/auth/switch-gym", new SwitchGymRequest(accessA.AccessId))).StatusCode);
    }

    [Fact]
    public async Task Switch_gym_also_moves_between_the_staff_and_member_accesses_of_the_same_gym()
    {
        var w = await SeedAsync(sharedPerson: false);
        await GrantAsync(w.PersonId, w.GymA, UserRole.Member);
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();

        var login = await LoginAsync(anonymous, w.PersonEmail);
        Assert.True(login.RequiresGymSelection);
        Assert.Equal(new[] { "member", "owner" }, login.Accesses!.Select(a => a.Role).Order().ToArray());

        var staffSide = login.Accesses!.Single(a => a.Role == "owner");
        var memberSide = login.Accesses!.Single(a => a.Role == "member");
        var tokens = await SelectAsync(anonymous, login.SelectionToken!, staffSide.AccessId);

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var switched = await client.PostAsJsonAsync("/api/auth/switch-gym", new SwitchGymRequest(memberSide.AccessId));
        switched.EnsureSuccessStatusCode();
        var claims = Claims((await switched.Content.ReadFromJsonAsync<AuthTokensResponse>())!.AccessToken);
        Assert.Equal("member", claims.GetProperty("role").GetString());
        Assert.Equal(w.GymA.ToString(), claims.GetProperty("gym_id").GetString());
    }

    [Fact]
    public async Task Mode_switch_never_mixes_roles_in_a_token_and_retires_the_session_it_leaves()
    {
        // AP4: modo staff <-> modo miembro en el mismo gym.
        var w = await SeedAsync(sharedPerson: false);
        await GrantAsync(w.PersonId, w.GymA, UserRole.Member);
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();
        var login = await LoginAsync(anonymous, w.PersonEmail);
        var teamSide = login.Accesses!.Single(a => a.Role == "owner");
        var memberSide = login.Accesses!.Single(a => a.Role == "member");
        var teamTokens = await SelectAsync(anonymous, login.SelectionToken!, teamSide.AccessId);

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", teamTokens.AccessToken);

        // En modo staff el menú marca el acceso de equipo como actual y ofrece el de miembro del mismo gym.
        var inTeamMode = await client.GetFromJsonAsync<List<GymAccessOption>>("/api/auth/accesses");
        Assert.True(inTeamMode!.Single(a => a.Role == "owner").IsCurrent);
        Assert.False(inTeamMode!.Single(a => a.Role == "member").IsCurrent);
        Assert.Equal((await client.GetAsync("/api/gym/staff")).StatusCode, HttpStatusCode.OK);

        var toMember = await client.PostAsJsonAsync(
            "/api/auth/switch-gym", new SwitchGymRequest(memberSide.AccessId, teamTokens.RefreshToken));
        toMember.EnsureSuccessStatusCode();
        var memberTokens = (await toMember.Content.ReadFromJsonAsync<AuthTokensResponse>())!;
        var claims = Claims(memberTokens.AccessToken);
        Assert.Equal("member", claims.GetProperty("role").GetString());

        // El token de miembro no abre lo del staff, aunque la persona sea Owner de ese gym.
        var asMember = host.CreateClient();
        asMember.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberTokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await asMember.GetAsync("/api/gym/staff")).StatusCode);
        var inMemberMode = await asMember.GetFromJsonAsync<List<GymAccessOption>>("/api/auth/accesses");
        Assert.True(inMemberMode!.Single(a => a.Role == "member").IsCurrent);

        // La sesión de staff que se dejó no se renueva; la de miembro sí, y sigue siendo de miembro.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(teamTokens.RefreshToken))).StatusCode);
        var renewed = await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(memberTokens.RefreshToken));
        renewed.EnsureSuccessStatusCode();
        Assert.Equal(
            "member",
            Claims((await renewed.Content.ReadFromJsonAsync<AuthTokensResponse>())!.AccessToken).GetProperty("role").GetString());
    }

    [Fact]
    public async Task A_person_can_hold_a_team_access_and_a_member_access_in_a_gym_but_not_two_team_accesses()
    {
        var w = await SeedAsync(sharedPerson: false);
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        // Dos accesos (equipo + miembro) en el mismo gym: se admite.
        await provisioning.CreateMemberUserAsync(w.PersonEmail, "Persona", Password, w.GymA);

        // Un segundo acceso de miembro, o un tercero de equipo: 409.
        await Assert.ThrowsAsync<ConflictException>(
            () => provisioning.CreateMemberUserAsync(w.PersonEmail, "Persona", Password, w.GymA));
        await Assert.ThrowsAsync<ConflictException>(
            () => provisioning.CreateGymUserAsync(w.PersonEmail, "Persona", Password, UserRole.Staff, w.GymA));

        // Y por HTTP: dar de alta como staff a quien ya es owner del gym.
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();
        var login = await LoginAsync(anonymous, w.PersonEmail);
        var owner = await SelectAsync(
            anonymous, login.SelectionToken!, login.Accesses!.Single(a => a.Role == "owner").AccessId);
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var dup = await client.PostAsJsonAsync("/api/gym/staff", new CreateStaffRequest(w.PersonEmail, "Persona"));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Deactivating_staff_in_gym_A_keeps_gym_B_and_revokes_only_the_gym_A_sessions()
    {
        var w = await SeedAsync(sharedPerson: false);
        // La persona es staff en A (donde el owner es otro) y staff en B.
        var staffEmail = $"multi-staff-{Guid.NewGuid():N}"[..24] + "@test.local";
        var staffId = await ProvisionAsync(staffEmail, UserRole.Staff, w.GymA);
        await GrantAsync(staffId, w.GymB, UserRole.Staff);
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();

        var login = await LoginAsync(anonymous, staffEmail);
        var accessA = login.Accesses!.Single(a => a.GymId == w.GymA);
        var accessB = login.Accesses!.Single(a => a.GymId == w.GymB);
        var sessionA = await SelectAsync(anonymous, login.SelectionToken!, accessA.AccessId);
        var sessionB = await anonymous.PostAsJsonAsync("/api/auth/select-gym",
            new SelectGymRequest(login.SelectionToken!, accessB.AccessId));
        var tokensB = (await sessionB.Content.ReadFromJsonAsync<AuthTokensResponse>())!;

        // El owner de A (la persona del seed) lo desactiva.
        var ownerLogin = await LoginAsync(host.CreateClient(), w.PersonEmail);
        var owner = host.CreateClient();
        owner.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ownerLogin.Tokens!.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/gym/staff/{staffId}")).StatusCode);

        // Sus sesiones de A dejan de renovarse; las de B siguen.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(sessionA.RefreshToken))).StatusCode);
        var refreshB = await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokensB.RefreshToken));
        refreshB.EnsureSuccessStatusCode();

        // Y al volver a entrar ya solo tiene B: entra directo.
        var again = await LoginAsync(anonymous, staffEmail);
        Assert.False(again.RequiresGymSelection);
        Assert.Equal(w.GymB.ToString(), Claims(again.Tokens!.AccessToken).GetProperty("gym_id").GetString());
    }

    [Fact]
    public async Task Refresh_reissues_the_token_for_the_same_access()
    {
        var w = await SeedAsync(sharedPerson: true);
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();
        var login = await LoginAsync(anonymous, w.PersonEmail);
        var accessB = login.Accesses!.Single(a => a.GymId == w.GymB);
        var tokens = await SelectAsync(anonymous, login.SelectionToken!, accessB.AccessId);

        var refreshed = await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokens.RefreshToken));
        refreshed.EnsureSuccessStatusCode();
        var claims = Claims((await refreshed.Content.ReadFromJsonAsync<AuthTokensResponse>())!.AccessToken);

        Assert.Equal(w.GymB.ToString(), claims.GetProperty("gym_id").GetString());
        Assert.Equal("staff", claims.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Staff_created_with_an_existing_email_only_adds_the_access()
    {
        var w = await SeedAsync(sharedPerson: false);
        var existingEmail = $"existing-{Guid.NewGuid():N}"[..20] + "@test.local";
        var existingId = await ProvisionAsync(existingEmail, UserRole.Owner, w.GymB);
        await using var host = RealIdentityHost();
        var login = await LoginAsync(host.CreateClient(), w.PersonEmail);
        var owner = host.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Tokens!.AccessToken);

        var created = await owner.PostAsJsonAsync("/api/gym/staff", new CreateStaffRequest(existingEmail, "Otro Nombre"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(existingId, (await created.Content.ReadFromJsonAsync<StaffResponse>())!.Id);
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == existingEmail));
        Assert.Equal(2, await db.GymAccesses.CountAsync(a => a.UserId == existingId));

        // Y con su contraseña de siempre entra a elegir entre sus dos gyms.
        var relogin = await LoginAsync(host.CreateClient(), existingEmail);
        Assert.True(relogin.RequiresGymSelection);
    }

    [Fact]
    public async Task Member_invitation_for_an_existing_account_adds_the_access_validates_the_password_and_starts_the_session()
    {
        var w = await SeedAsync(sharedPerson: false);
        // La persona ya es miembro del gym B con su cuenta.
        var memberEmail = $"member-{Guid.NewGuid():N}"[..20] + "@test.local";
        var existingId = await ProvisionMemberAsync(memberEmail, w.GymB);
        var (memberId, code) = await InviteAsync(w, memberEmail, "doc-existing");
        await using var host = RealIdentityHost();
        var anonymous = host.CreateClient();

        // Contraseña incorrecta: 401 y no queda nada a medias.
        var wrong = await anonymous.PostAsJsonAsync("/api/members/register",
            new RegisterMemberRequest(code, "doc-existing", null, "WrongPass_12345"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        // Con su contraseña: acceso nuevo, ficha vinculada y sesión iniciada. El email sale de la ficha.
        var ok = await anonymous.PostAsJsonAsync("/api/members/register",
            new RegisterMemberRequest(code, "doc-existing", null, Password));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var body = (await ok.Content.ReadFromJsonAsync<RegisterMemberResponse>())!;
        Assert.Equal(existingId, body.UserId);
        var claims = Claims(body.Session.AccessToken);
        Assert.Equal("member", claims.GetProperty("role").GetString());
        Assert.Equal(w.GymA.ToString(), claims.GetProperty("gym_id").GetString());

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == memberEmail));
        Assert.Equal(2, await db.GymAccesses.CountAsync(a => a.UserId == existingId));
        Assert.Equal(existingId, (await db.Set<Member>().SingleAsync(m => m.Id == memberId)).UserId);
    }

    [Fact]
    public async Task Member_invitation_for_a_new_email_creates_the_account_and_starts_the_session()
    {
        var w = await SeedAsync(sharedPerson: false);
        var email = $"new-{Guid.NewGuid():N}"[..20] + "@test.local";
        var (_, code) = await InviteAsync(w, email, "doc-new");
        await using var host = RealIdentityHost();

        var ok = await host.CreateClient().PostAsJsonAsync("/api/members/register",
            new RegisterMemberRequest(code, "doc-new", null, Password));

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var body = (await ok.Content.ReadFromJsonAsync<RegisterMemberResponse>())!;
        Assert.False(string.IsNullOrEmpty(body.Session.RefreshToken));
        Assert.Equal("member", Claims(body.Session.AccessToken).GetProperty("role").GetString());

        // La sesión sirve: el miembro lee su propio perfil.
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Member_invitation_asks_for_the_email_only_when_the_record_has_none()
    {
        var w = await SeedAsync(sharedPerson: false);
        var (_, code) = await InviteAsync(w, email: null, "doc-noemail");
        await using var host = RealIdentityHost();

        var missing = await host.CreateClient().PostAsJsonAsync("/api/members/register",
            new RegisterMemberRequest(code, "doc-noemail", null, Password));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);

        var email = $"asked-{Guid.NewGuid():N}"[..20] + "@test.local";
        var ok = await host.CreateClient().PostAsJsonAsync("/api/members/register",
            new RegisterMemberRequest(code, "doc-noemail", email, Password));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Impersonation_token_has_no_accesses_and_cannot_switch_gym()
    {
        var w = await SeedAsync(sharedPerson: false);
        await using var host = RealIdentityHost();
        var token = host.Services.GetRequiredService<IJwtTokenService>().CreateImpersonationToken(
            new ImpersonationTokenRequest(Guid.CreateVersion7(), "Soporte", w.GymA, Guid.CreateVersion7()));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/gym")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<GymAccessOption>>("/api/auth/accesses"))!);
    }

    [Fact]
    public async Task Superadmin_logs_in_without_accesses()
    {
        var email = $"sa-{Guid.NewGuid():N}"[..16] + "@test.local";
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            // S5: solo puede haber un SuperAdmin; se retira el de corridas anteriores.
            await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Database
                .ExecuteSqlRawAsync("delete from identity.users where is_super_admin");
            await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
                .CreateSuperAdminAsync(email, "Root", Password);
        }

        await using var host = RealIdentityHost();
        var login = await LoginAsync(host.CreateClient(), email);

        Assert.False(login.RequiresGymSelection);
        Assert.Equal("superadmin", Claims(login.Tokens!.AccessToken).GetProperty("role").GetString());
        var refreshed = await host.CreateClient().PostAsJsonAsync(
            "/api/auth/refresh", new RefreshRequest(login.Tokens.RefreshToken));
        refreshed.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Migration_turns_existing_accounts_into_gym_accesses()
    {
        const string name = "gsm_test_identidad_i2mig";
        var admin = $"Host=localhost;Port=5432;Database=postgres;Username=gsm;Password=gsm";
        var conn = $"Host=localhost;Port=5432;Database={name};Username=gsm;Password=gsm";

        await using (var data = Npgsql.NpgsqlDataSource.Create(admin))
        {
            await using var cmd = data.CreateCommand($"drop database if exists {name} with (force)");
            await cmd.ExecuteNonQueryAsync();
            await using var create = data.CreateCommand($"create database {name}");
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<GsmDbContext>()
                .UseNpgsql(conn, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(GsmDbContext).Assembly.FullName);
                    PgEnumMappings.ConfigureProvider(npgsql);
                })
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new GsmDbContext(options, GSM.Infrastructure.Common.NullCurrentUser.Instance);
            var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();

            // Estado anterior a I2, con los usuarios del modelo viejo.
            await migrator.MigrateAsync("20261008020534_V2_LG1_Logs");
            var gym = Guid.CreateVersion7();
            var (sa, owner, staff, oldStaff, member) =
                (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
            await db.Database.ExecuteSqlRawAsync($"""
                insert into identity.users(id,full_name,gym_id,role,is_active,user_name,normalized_user_name,email,normalized_email,email_confirmed,phone_number_confirmed,two_factor_enabled,lockout_enabled,access_failed_count) values
                ('{sa}','Root',null,'superadmin',true,'sa','SA','sa@x','SA@X',true,false,false,false,0),
                ('{owner}','Owner','{gym}','owner',true,'o','O','o@x','O@X',true,false,false,false,0),
                ('{staff}','Staff','{gym}','staff',true,'s','S','s@x','S@X',true,false,false,false,0),
                ('{oldStaff}','Old','{gym}','staff',false,'os','OS','os@x','OS@X',true,false,false,false,0),
                ('{member}','Member','{gym}','member',true,'m','M','m@x','M@X',true,false,false,false,0);
                insert into identity.refresh_tokens(id,user_id,token_hash,expires_at,created_at,updated_at) values
                ('{Guid.CreateVersion7()}','{staff}','h-staff',now()+interval '1 day',now(),now());
                """);

            await migrator.MigrateAsync();

            var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id);
            Assert.True(users[sa].IsSuperAdmin);
            Assert.Empty(await db.GymAccesses.Where(a => a.UserId == sa).ToListAsync());

            var accesses = await db.GymAccesses.AsNoTracking().ToListAsync();
            Assert.Equal(4, accesses.Count);
            Assert.All(accesses, a => Assert.Equal(gym, a.GymId));
            Assert.Equal(UserRole.Owner, accesses.Single(a => a.UserId == owner).Role);
            Assert.Equal(UserRole.Staff, accesses.Single(a => a.UserId == staff).Role);
            Assert.True(accesses.Single(a => a.UserId == staff).IsTeam);
            Assert.Equal(UserRole.Member, accesses.Single(a => a.UserId == member).Role);
            Assert.False(accesses.Single(a => a.UserId == member).IsTeam);

            // El staff dado de baja queda con el acceso inactivo y la cuenta activa.
            Assert.False(accesses.Single(a => a.UserId == oldStaff).IsActive);
            Assert.True(users[oldStaff].IsActive);

            // La sesión abierta se ata al único acceso de su dueño.
            Assert.Equal(
                accesses.Single(a => a.UserId == staff).Id,
                (await db.RefreshTokens.AsNoTracking().SingleAsync()).GymAccessId);
        }
        finally
        {
            await using var data = Npgsql.NpgsqlDataSource.Create(admin);
            await using var cmd = data.CreateCommand($"drop database if exists {name} with (force)");
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ---- helpers ----

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static async Task<AuthTokensResponse> SelectAsync(HttpClient client, string ticket, Guid accessId)
    {
        var response = await client.PostAsJsonAsync("/api/auth/select-gym", new SelectGymRequest(ticket, accessId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokensResponse>())!;
    }

    private static JsonElement Claims(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement;
    }

    private async Task<Guid> ProvisionAsync(string email, UserRole role, Guid gymId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
            .CreateGymUserAsync(email, "Persona Multi", Password, role, gymId);
    }

    private async Task<Guid> ProvisionMemberAsync(string email, Guid gymId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
            .CreateMemberUserAsync(email, "Persona Miembro", Password, gymId);
    }

    /// <summary>Agrega un acceso a una cuenta existente (por su id), sin pasar por contraseña.</summary>
    private async Task GrantAsync(Guid userId, Guid gymId, UserRole role)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        db.GymAccesses.Add(GymAccess.Grant(userId, gymId, role));
        await db.SaveChangesAsync();
    }

    private async Task SetAccessActiveAsync(Guid accessId, bool active)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var access = await db.GymAccesses.SingleAsync(a => a.Id == accessId);
        if (active)
        {
            access.Reactivate();
        }
        else
        {
            access.Deactivate(DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Crea una ficha en el gym A con el documento dado y emite su invitación.</summary>
    private async Task<(Guid MemberId, string Code)> InviteAsync(World w, string? email, string document)
    {
        using var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IClock>().Today;
        var member = Member.Create(w.GymA, "Mia", "Miembro", $"{document}", today, email: email);
        db.Add(member);
        await db.SaveChangesAsync();

        user.ActAs(w.PersonId, w.GymA, UserRole.Staff);
        var invite = await scope.ServiceProvider.GetRequiredService<InviteMemberHandler>()
            .HandleAsync(member.Id, CancellationToken.None);
        return (member.Id, invite.Code);
    }

    /// <summary>
    /// Dos gyms (A y B) con un miembro cada uno. La persona es owner de A y, si
    /// <paramref name="sharedPerson"/>, también staff de B con la misma cuenta.
    /// </summary>
    private async Task<World> SeedAsync(bool sharedPerson)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var today = scope.ServiceProvider.GetRequiredService<IClock>().Today;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gymA = Gym.Create("Gym A", $"multi-a-{suffix}");
        var gymB = Gym.Create("Gym B", $"multi-b-{suffix}");
        var memberA = Member.Create(gymA.Id, "Ana", "A", $"da-{suffix}", today);
        var memberB = Member.Create(gymB.Id, "Beto", "B", $"db-{suffix}", today);
        db.AddRange(gymA, GymFeatureFlags.Default(gymA.Id), gymB, GymFeatureFlags.Default(gymB.Id), memberA, memberB);
        await db.SaveChangesAsync();

        var email = $"person-{suffix}@test.local";
        var personId = await provisioning.CreateGymUserAsync(email, "Persona", Password, UserRole.Owner, gymA.Id);
        gymA.AssignOwner(personId);
        if (sharedPerson)
        {
            await provisioning.CreateGymUserAsync(email, "Persona", Password, UserRole.Staff, gymB.Id);
            gymB.AssignOwner(personId);
        }
        else
        {
            gymB.AssignOwner(Guid.CreateVersion7());
        }

        await db.SaveChangesAsync();
        return new World(personId, email, gymA.Id, gymB.Id, memberA.Id, memberB.Id);
    }

    private sealed record World(Guid PersonId, string PersonEmail, Guid GymA, Guid GymB, Guid MemberA, Guid MemberB);
}
