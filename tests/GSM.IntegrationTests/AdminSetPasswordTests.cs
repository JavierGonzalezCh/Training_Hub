using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
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
/// PW1: el SuperAdmin lista las cuentas de un gym y fija la contraseña de cualquiera (owner, staff o miembro) sin
/// correo. La clave nueva sirve para entrar, las sesiones anteriores se cierran y queda la auditoría.
/// </summary>
public sealed class AdminSetPasswordTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private const string NewPassword = "Nueva_Clave_123";

    [Fact]
    public async Task The_super_admin_lists_every_account_of_the_gym_and_filters_them()
    {
        var w = await SeedAsync();
        using var scope = SuperAdminScope(Guid.CreateVersion7());
        var list = scope.ServiceProvider.GetRequiredService<ListGymAccountsHandler>();

        var all = await list.HandleAsync(w.GymId, search: null, page: 1, pageSize: 20, default);
        Assert.Equal(3, all.Total);
        Assert.Equal(["owner", "staff", "member"], all.Items.Select(a => a.Role));
        Assert.All(all.Items, a => Assert.True(a.HasPassword));

        var filtered = await list.HandleAsync(w.GymId, search: "MIEMBRO", page: 1, pageSize: 20, default);
        Assert.Equal(w.MemberId, Assert.Single(filtered.Items).UserId);
    }

    [Fact]
    public async Task Setting_a_password_lets_the_person_in_closes_old_sessions_and_is_audited()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var client = host.CreateClient();
        var oldTokens = await SignInAsync(client, w.MemberEmail, Password);

        var actorId = Guid.CreateVersion7();
        using (var scope = SuperAdminScope(actorId))
        {
            await scope.ServiceProvider.GetRequiredService<AdminSetPasswordHandler>()
                .HandleAsync(w.GymId, w.MemberId, new AdminSetPasswordRequest(NewPassword), default);

            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var audit = await db.StaffAccessAudits.AsNoTracking()
                .SingleAsync(a => a.ActorUserId == actorId && a.Action == StaffAccessAuditAction.PasswordSetByAdmin);
            Assert.Equal(w.GymId, audit.GymId);
            Assert.Equal(w.MemberId, audit.TargetUserId);
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.MemberEmail, Password))).StatusCode);
        Assert.NotNull(await SignInAsync(client, w.MemberEmail, NewPassword));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(oldTokens.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task A_pending_owner_gets_activated_and_a_locked_account_is_unlocked()
    {
        var w = await SeedAsync();
        var pending = await SeedPendingOwnerAsync(w.GymId);

        using (var lockScope = factory.CreateScopeWithMigration())
        {
            var users = lockScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var staff = (await users.FindByIdAsync(w.StaffId.ToString()))!;
            await users.SetLockoutEndDateAsync(staff, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        using (var scope = SuperAdminScope(Guid.CreateVersion7()))
        {
            var set = scope.ServiceProvider.GetRequiredService<AdminSetPasswordHandler>();
            await set.HandleAsync(w.GymId, pending.UserId, new AdminSetPasswordRequest(NewPassword), default);
            await set.HandleAsync(w.GymId, w.StaffId, new AdminSetPasswordRequest(NewPassword), default);
        }

        await using var host = RealIdentityHost();
        var client = host.CreateClient();
        Assert.NotNull(await SignInAsync(client, pending.Email, NewPassword));
        Assert.NotNull(await SignInAsync(client, w.StaffEmail, NewPassword));
    }

    [Fact]
    public async Task Rejects_a_weak_password_and_a_person_from_another_gym()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        using var scope = SuperAdminScope(Guid.CreateVersion7());
        var set = scope.ServiceProvider.GetRequiredService<AdminSetPasswordHandler>();

        await Assert.ThrowsAsync<ValidationDomainException>(
            () => set.HandleAsync(w.GymId, w.MemberId, new AdminSetPasswordRequest("solominusculas"), default));
        await Assert.ThrowsAsync<NotFoundException>(
            () => set.HandleAsync(w.GymId, other.MemberId, new AdminSetPasswordRequest(NewPassword), default));
    }

    [Fact]
    public async Task Only_the_super_admin_reaches_the_endpoints()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = host.CreateClient();
        var tokens = await SignInAsync(owner, w.OwnerEmail, Password);
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/admin/gyms/{w.GymId}/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.PutAsJsonAsync(
                $"/api/admin/gyms/{w.GymId}/accounts/{w.MemberId}/password",
                new AdminSetPasswordRequest(NewPassword))).StatusCode);
    }

    // ---- Infraestructura de los tests ----

    private IServiceScope SuperAdminScope(Guid actorId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(actorId, gymId: null, UserRole.SuperAdmin);
        return scope;
    }

    private static async Task<AuthTokensResponse> SignInAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Tokens!;
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
        var gym = Gym.Create("Claves Gym", $"pw-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var memberEmail = $"member-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Claves Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Claves Staff", Password, UserRole.Staff, gym.Id);
        var memberId = await provisioning.CreateMemberUserAsync(memberEmail, "Claves Miembro", Password, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerEmail, staffId, staffEmail, memberId, memberEmail);
    }

    private async Task<(Guid UserId, string Email)> SeedPendingOwnerAsync(Guid gymId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var email = $"pend-{Guid.NewGuid():N}"[..13] + "@test.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Owner Pendiente", IsActive = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        db.GymAccesses.Add(GymAccess.Grant(user.Id, gymId, UserRole.Owner));
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    private sealed record World(
        Guid GymId, string OwnerEmail, Guid StaffId, string StaffEmail, Guid MemberId, string MemberEmail);
}
