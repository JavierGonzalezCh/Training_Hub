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
                npgsql.MapEnum<UserRole>("user_role", "identity", LowerCaseNameTranslator.Instance);
                npgsql.MapEnum<GymPlan>("gym_plan", "gyms", LowerCaseNameTranslator.Instance);
                npgsql.MapEnum<GymStatus>("gym_status", "gyms", LowerCaseNameTranslator.Instance);
                npgsql.MapEnum<MemberStatus>("member_status", "members", LowerCaseNameTranslator.Instance);
                npgsql.MapEnum<MembershipStatus>("membership_status", "memberships", LowerCaseNameTranslator.Instance);
                npgsql.MapEnum<PaymentMethod>("payment_method", "payments", LowerCaseNameTranslator.Instance);
                npgsql.MapEnum<AttendanceMethod>("attendance_method", "attendance", LowerCaseNameTranslator.Instance);
                npgsql.MapEnum<AchievementConditionType>("condition_type", "achievements", LowerCaseNameTranslator.Instance);
                npgsql.MapEnum<Muscle>("muscle", "routines", LowerCaseNameTranslator.Instance);
                npgsql.MapEnum<Equipment>("equipment", "routines", LowerCaseNameTranslator.Instance);
            })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GsmDbContext(options, NullCurrentUser.Instance);
    }
}
