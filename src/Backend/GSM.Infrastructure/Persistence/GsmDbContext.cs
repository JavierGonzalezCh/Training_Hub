using System.Linq.Expressions;
using GSM.Application.Common.Abstractions;
using GSM.Domain.Common;
using GSM.Domain.Identity;
using GSM.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Persistence;

/// <summary>
/// DbContext único del monolito. Cada módulo aporta sus <see cref="IEntityTypeConfiguration{TEntity}"/>
/// en su carpeta de Infrastructure; aquí solo se aplican en bloque más las reglas transversales:
/// snake_case, Global Query Filter multi-tenant y timestamps automáticos.
/// </summary>
/// <remarks>
/// <see cref="ICurrentUserService"/> debe registrarse como singleton (respaldado por
/// <c>IHttpContextAccessor</c>): el modelo de EF se compila una sola vez y captura esta
/// referencia, pero sus getters leen el contexto de la petición vigente en cada query.
/// </remarks>
public class GsmDbContext(DbContextOptions<GsmDbContext> options, ICurrentUserService currentUser)
    : IdentityUserContext<AppUser, Guid>(options)
{
    private readonly ICurrentUserService _currentUser = currentUser;

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresEnum<UserRole>("identity", "user_role", LowerCaseNameTranslator.Instance);

        // Tablas satélite de Identity → schema y nombres del proyecto.
        modelBuilder.Entity<AppUser>().ToTable("users", "identity");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", "identity");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", "identity");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", "identity");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GsmDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Property(e => e.CreatedAt).IsModified = false;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Para cada entidad que implemente <see cref="ITenantEntity"/>, restringe las consultas
    /// al gym actual. Si el usuario es SuperAdmin o no hay gym en contexto (seeds, jobs),
    /// el filtro se vuelve transparente.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var currentUserConst = Expression.Constant(_currentUser);
        var bypass = Expression.OrElse(
            Expression.Property(currentUserConst, nameof(ICurrentUserService.IsSuperAdmin)),
            Expression.Equal(
                Expression.Property(currentUserConst, nameof(ICurrentUserService.GymId)),
                Expression.Constant(null, typeof(Guid?))));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var entityGymId = Expression.Property(parameter, nameof(ITenantEntity.GymId));
            var currentGymId = Expression.Property(currentUserConst, nameof(ICurrentUserService.GymId));

            var sameTenant = Expression.Equal(
                Expression.Convert(entityGymId, typeof(Guid?)),
                currentGymId);

            var body = Expression.OrElse(bypass, sameTenant);

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}
