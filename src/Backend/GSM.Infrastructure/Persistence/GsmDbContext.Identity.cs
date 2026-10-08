using GSM.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Persistence;

// DbSets del módulo Identity. Cada módulo añade aquí los suyos en su propio archivo parcial
// (GsmDbContext.{Módulo}.cs); las entidades sin DbSet se acceden con Set<T>() y se configuran
// con IEntityTypeConfiguration en la carpeta del módulo.
public partial class GsmDbContext
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<GymAccess> GymAccesses => Set<GymAccess>();

    public DbSet<GymAccessPermission> GymAccessPermissions => Set<GymAccessPermission>();

    public DbSet<StaffAccessAudit> StaffAccessAudits => Set<StaffAccessAudit>();
}
