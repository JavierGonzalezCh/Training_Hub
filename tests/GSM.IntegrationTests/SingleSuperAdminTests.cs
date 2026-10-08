using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>S5: solo una cuenta puede ser SuperAdmin; la base lo garantiza con un índice único parcial.</summary>
public sealed class SingleSuperAdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string NewEmail() => $"sa-{Guid.NewGuid():N}"[..16] + "@test.local";

    private async Task<(IServiceScope Scope, GsmDbContext Db)> ArrangeWithOneSuperAdminAsync()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync("delete from identity.users where is_super_admin");
        await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
            .CreateSuperAdminAsync(NewEmail(), "Root", "AdminPass_123");
        return (scope, db);
    }

    [Fact]
    public async Task Creating_a_second_superadmin_is_a_conflict_and_the_database_keeps_one()
    {
        var (scope, db) = await ArrangeWithOneSuperAdminAsync();
        using var _ = scope;

        await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
            .GetRequiredService<IUserProvisioning>()
            .CreateSuperAdminAsync(NewEmail(), "Otro", "AdminPass_123"));

        Assert.Equal(1, await db.Users.IgnoreQueryFilters().CountAsync(u => u.IsSuperAdmin));
    }

    [Fact]
    public async Task The_partial_unique_index_rejects_marking_a_second_account_directly()
    {
        var (scope, db) = await ArrangeWithOneSuperAdminAsync();
        using var _ = scope;

        var id = Guid.NewGuid();
        var email = NewEmail();
        await db.Database.ExecuteSqlRawAsync(
            """
            insert into identity.users (id, user_name, normalized_user_name, email, normalized_email, email_confirmed,
              full_name, is_super_admin, is_active, access_failed_count, lockout_enabled, phone_number_confirmed,
              two_factor_enabled, security_stamp, concurrency_stamp)
            values ({0}, {1}, upper({1}), {1}, upper({1}), true, 'Otra', false, true, 0, false, false, false, 'x', 'x')
            """, id, email);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "update identity.users set is_super_admin = true where id = {0}", id));
        Assert.Equal("23505", ex.SqlState);
        Assert.Equal("ux_users_single_super_admin", ex.ConstraintName);
    }
}
