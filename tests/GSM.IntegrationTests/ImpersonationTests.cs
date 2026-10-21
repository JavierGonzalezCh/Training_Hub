using GSM.Application.Identity;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Etapa 11.5: impersonación de solo lectura, de vida corta y auditada. La identidad real del
/// superadmin nunca se suplanta.
/// </summary>
public sealed class ImpersonationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Opening_without_a_reason_is_rejected()
    {
        var w = await SeedAsync();
        using var scope = SuperAdmin(w.AdminId);

        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<OpenImpersonationHandler>()
                .HandleAsync(w.GymA, new OpenImpersonationRequest("  "), default));
    }

    [Fact]
    public async Task Opening_issues_a_15_minute_token_that_keeps_the_real_identity()
    {
        var w = await SeedAsync();

        ImpersonationTokenResponse token;
        using (var scope = SuperAdmin(w.AdminId))
        {
            token = await scope.ServiceProvider.GetRequiredService<OpenImpersonationHandler>()
                .HandleAsync(w.GymA, new OpenImpersonationRequest("Soporte ticket #42"), default);
        }

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Token);
        Assert.Equal(w.AdminId.ToString(), jwt.GetClaim("sub").Value);
        Assert.Equal(w.GymA.ToString(), jwt.GetClaim("gym_id").Value);
        Assert.Equal("true", jwt.GetClaim("impersonating").Value);
        Assert.Equal(token.SessionId.ToString(), jwt.GetClaim("impersonation_session").Value);
        Assert.Equal("superadmin", jwt.GetClaim("role").Value);

        var minutes = (token.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalMinutes;
        Assert.InRange(minutes, 13, 16);

        using (var check = SuperAdmin(w.AdminId))
        {
            var session = await check.ServiceProvider.GetRequiredService<GsmDbContext>()
                .Set<ImpersonationSession>().SingleAsync(s => s.Id == token.SessionId);
            Assert.Equal(w.AdminId, session.SuperAdminUserId);
            Assert.Equal(w.GymA, session.GymId);
            Assert.Null(session.EndedAt);
        }
    }

    [Fact]
    public async Task An_impersonation_token_only_sees_the_visited_gym()
    {
        var w = await SeedAsync();

        Guid sessionId;
        using (var scope = SuperAdmin(w.AdminId))
        {
            sessionId = (await scope.ServiceProvider.GetRequiredService<OpenImpersonationHandler>()
                .HandleAsync(w.GymA, new OpenImpersonationRequest("Soporte"), default)).SessionId;
        }

        using var visiting = factory.CreateScopeWithMigration();
        visiting.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAsImpersonator(w.AdminId, w.GymA, sessionId);
        var db = visiting.ServiceProvider.GetRequiredService<GsmDbContext>();

        var visibleGyms = await db.Set<Gym>().Select(g => g.Id).ToListAsync();
        var visibleMemberGyms = await db.Set<Member>().Select(m => m.GymId).Distinct().ToListAsync();

        Assert.Equal([w.GymA], visibleGyms);          // el Global Query Filter vuelve a aplicar
        Assert.Equal([w.GymA], visibleMemberGyms);
        Assert.False(await db.Set<Gym>().AnyAsync(g => g.Id == w.GymB));
    }

    [Fact]
    public async Task Ending_closes_the_session_and_a_second_end_is_not_found()
    {
        var w = await SeedAsync();

        Guid sessionId;
        using (var scope = SuperAdmin(w.AdminId))
        {
            sessionId = (await scope.ServiceProvider.GetRequiredService<OpenImpersonationHandler>()
                .HandleAsync(w.GymA, new OpenImpersonationRequest("Soporte"), default)).SessionId;
        }

        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
                .ActAsImpersonator(w.AdminId, w.GymA, sessionId);
            await scope.ServiceProvider.GetRequiredService<EndImpersonationHandler>().HandleAsync(default);
        }

        using (var check = SuperAdmin(w.AdminId))
        {
            var session = await check.ServiceProvider.GetRequiredService<GsmDbContext>()
                .Set<ImpersonationSession>().SingleAsync(s => s.Id == sessionId);
            Assert.NotNull(session.EndedAt);
        }

        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
                .ActAsImpersonator(w.AdminId, w.GymA, sessionId);
            await Assert.ThrowsAsync<ConflictException>(() =>
                scope.ServiceProvider.GetRequiredService<EndImpersonationHandler>().HandleAsync(default));
        }
    }

    [Fact]
    public async Task History_records_who_where_when_and_why()
    {
        var w = await SeedAsync();
        using (var scope = SuperAdmin(w.AdminId))
        {
            await scope.ServiceProvider.GetRequiredService<OpenImpersonationHandler>()
                .HandleAsync(w.GymA, new OpenImpersonationRequest("Revisar reporte de bug"), default);
        }

        using var read = SuperAdmin(w.AdminId);
        var history = await read.ServiceProvider.GetRequiredService<ListImpersonationsHandler>()
            .HandleAsync(null, 50, default);

        var item = Assert.Single(history);
        Assert.Equal(w.AdminId, item.SuperAdminUserId);
        Assert.Equal(w.GymA, item.GymId);
        Assert.Equal("Gym A", item.GymName);
        Assert.Equal("Revisar reporte de bug", item.Reason);
    }

    [Fact]
    public async Task A_session_nobody_closed_shows_as_ended_once_its_token_expired()
    {
        var w = await SeedAsync();
        using (var scope = SuperAdmin(w.AdminId))
        {
            await scope.ServiceProvider.GetRequiredService<OpenImpersonationHandler>()
                .HandleAsync(w.GymA, new OpenImpersonationRequest("Se me olvidó finalizar"), default);
        }

        using var read = SuperAdmin(w.AdminId);
        var handler = read.ServiceProvider.GetRequiredService<ListImpersonationsHandler>();
        var open = Assert.Single(await handler.HandleAsync(null, 50, default));
        Assert.Null(open.EndedAt);

        var db = read.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE superadmin.impersonation_sessions SET started_at = now() - interval '20 minutes' WHERE gym_id = {0}", w.GymA);

        var expired = Assert.Single(await handler.HandleAsync(null, 50, default));
        Assert.NotNull(expired.EndedAt);
        Assert.Equal(expired.StartedAt.AddMinutes(15), expired.EndedAt);
    }

    private IServiceScope SuperAdmin(Guid adminId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(adminId, gymId: null, UserRole.SuperAdmin);
        return scope;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, " +
            "superadmin.gym_subscriptions, superadmin.impersonation_sessions restart identity cascade;");

        var admin = await scope.ServiceProvider
            .GetRequiredService<GSM.Application.Identity.Public.IUserProvisioning>()
            .CreateSuperAdminAsync($"sa-{Guid.NewGuid():N}"[..16] + "@test.local", "Super Admin", "AdminPass_123");

        var gymA = MakeGym(db, "Gym A", "gym-a");
        var gymB = MakeGym(db, "Gym B", "gym-b");
        db.Add(Member.Create(gymA, "Ana", "A", $"da-{Guid.NewGuid():N}"[..10], new DateOnly(2026, 1, 1)));
        db.Add(Member.Create(gymB, "Beto", "B", $"db-{Guid.NewGuid():N}"[..10], new DateOnly(2026, 1, 1)));

        await db.SaveChangesAsync();
        return new World(admin, gymA, gymB);
    }

    private static Guid MakeGym(GsmDbContext db, string name, string slugPrefix)
    {
        var gym = Gym.Create(name, $"{slugPrefix}-{Guid.NewGuid():N}"[..14]);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        return gym.Id;
    }

    private sealed record World(Guid AdminId, Guid GymA, Guid GymB);
}
