using System.Linq.Expressions;
using GSM.Application.Common.Abstractions;
using GSM.Domain.Common;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Achievements;
using GSM.Domain.Attendance;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Payments;
using GSM.Domain.Routines;
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
        modelBuilder.HasPostgresEnum<GymPlan>("gyms", "gym_plan", LowerCaseNameTranslator.Instance);
        modelBuilder.HasPostgresEnum<GymStatus>("gyms", "gym_status", LowerCaseNameTranslator.Instance);
        modelBuilder.HasPostgresEnum<MemberStatus>("members", "member_status", LowerCaseNameTranslator.Instance);
        modelBuilder.HasPostgresEnum<MembershipStatus>("memberships", "membership_status", LowerCaseNameTranslator.Instance);
        modelBuilder.HasPostgresEnum<PaymentMethod>("payments", "payment_method", LowerCaseNameTranslator.Instance);
        modelBuilder.HasPostgresEnum<AttendanceMethod>("attendance", "attendance_method", LowerCaseNameTranslator.Instance);
        modelBuilder.HasPostgresEnum<AchievementConditionType>("achievements", "condition_type", SnakeCaseNameTranslator.Instance);
        modelBuilder.HasPostgresEnum<Muscle>("routines", "muscle", LowerCaseNameTranslator.Instance);
        modelBuilder.HasPostgresEnum<Equipment>("routines", "equipment", LowerCaseNameTranslator.Instance);

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

    // --- Soporte del Global Query Filter multi-tenant ---
    // El filtro debe leer estos miembros del CONTEXTO (no del servicio capturado): EF Core
    // parametriza los accesos a miembros del DbContext y los reevalúa en cada consulta, mientras
    // que una constante con el servicio se evaluaría una sola vez y "congelaría" el primer gym.

    /// <summary>true si el filtro no debe aplicarse: SuperAdmin o sin gym en contexto (seeds, jobs).</summary>
    public bool TenantFilterDisabled => _currentUser.IsSuperAdmin || _currentUser.GymId is null;

    /// <summary>Gym del usuario actual (o <see cref="Guid.Empty"/> si no aplica; en ese caso el filtro ya está desactivado).</summary>
    public Guid TenantFilterGymId => _currentUser.GymId ?? Guid.Empty;

    /// <summary>
    /// Para cada entidad que implemente <see cref="ITenantEntity"/>, restringe las consultas al gym actual.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var contextConst = Expression.Constant(this, typeof(GsmDbContext));
        var disabled = Expression.Property(contextConst, nameof(TenantFilterDisabled));
        var currentGymId = Expression.Property(contextConst, nameof(TenantFilterGymId));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");

            // La mayoría de entidades tienen una columna 'gym_id'. Gym implementa GymId de forma
            // explícita (=> Id): ahí el tenant es la propia PK, así que se filtra por 'Id'.
            var tenantMember = entityType.FindProperty(nameof(ITenantEntity.GymId)) is not null
                ? nameof(ITenantEntity.GymId)
                : nameof(BaseEntity.Id);

            var sameTenant = Expression.Equal(
                Expression.Property(parameter, tenantMember),
                currentGymId);

            var body = Expression.OrElse(disabled, sameTenant);

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}
