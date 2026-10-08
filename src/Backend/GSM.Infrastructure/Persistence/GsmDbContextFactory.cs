using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Achievements;
using GSM.Domain.Attendance;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Payments;
using GSM.Domain.Routines;
using GSM.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GSM.Infrastructure.Persistence;

/// <summary>
/// Fábrica en tiempo de diseño para <c>dotnet ef</c>. La connection string sale de la
/// variable de entorno <c>GSM_MIGRATIONS_CONNECTION</c> o cae a un valor local por defecto.
/// </summary>
public sealed class GsmDbContextFactory : IDesignTimeDbContextFactory<GsmDbContext>
{
    public GsmDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("GSM_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=gsm;Username=gsm;Password=gsm";

        var options = new DbContextOptionsBuilder<GsmDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(GsmDbContext).Assembly.FullName);
                PgEnumMappings.ConfigureProvider(npgsql);
            })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GsmDbContext(options, NullCurrentUser.Instance);
    }
}
