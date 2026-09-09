using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Public;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Public;
using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Public;
using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Public;
using GSM.Domain.Achievements;
using GSM.Domain.Attendance;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Payments;
using GSM.Domain.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Public;
using GSM.Infrastructure.Achievements;
using GSM.Infrastructure.Routines;
using GSM.Infrastructure.Attendance;
using GSM.Infrastructure.Common;
using GSM.Infrastructure.Email;
using GSM.Infrastructure.Gyms;
using GSM.Infrastructure.Identity;
using GSM.Infrastructure.Members;
using GSM.Infrastructure.Memberships;
using GSM.Infrastructure.Payments;
using GSM.Infrastructure.Persistence;
using GSM.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "Falta la connection string 'ConnectionStrings:Postgres'.");

        services.AddDbContext<GsmDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(GsmDbContext).Assembly.FullName);
                    npgsql.MapEnum<UserRole>("user_role", "identity", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<GymPlan>("gym_plan", "gyms", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<GymStatus>("gym_status", "gyms", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<MemberStatus>("member_status", "members", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<MembershipStatus>("membership_status", "memberships", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<PaymentMethod>("payment_method", "payments", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<AttendanceMethod>("attendance_method", "attendance", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<AchievementConditionType>("condition_type", "achievements", SnakeCaseNameTranslator.Instance);
                    npgsql.MapEnum<Muscle>("muscle", "routines", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<Equipment>("equipment", "routines", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<GSM.Domain.Classes.ClassDayOfWeek>("day_of_week", "classes", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<GSM.Domain.Classes.ClassSessionStatus>("class_session_status", "classes", LowerCaseNameTranslator.Instance);
                    npgsql.MapEnum<GSM.Domain.Classes.ClassBookingStatus>("class_booking_status", "classes", LowerCaseNameTranslator.Instance);
                })
                .UseSnakeCaseNamingConvention());

        services.AddOptions<ClockOptions>().Bind(configuration.GetSection(ClockOptions.SectionName));
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<IdentitySeedOptions>()
            .Bind(configuration.GetSection(IdentitySeedOptions.SectionName));

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<GsmDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserCredentialsService, UserCredentialsService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IIdentityReader, IdentityReader>();
        services.AddScoped<IUserProvisioning, UserProvisioning>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        AddFileStorage(services, configuration);
        AddEmail(services, configuration);

        services.AddScoped<IGymRepository, GymRepository>();
        services.AddScoped<IGymReader, GymReader>();

        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IMemberReader, MemberReader>();

        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IMembershipReader, MembershipReader>();

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentReader, PaymentReader>();

        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IAttendanceReader, AttendanceReader>();

        services.AddScoped<IAchievementRepository, AchievementRepository>();
        services.AddScoped<IAchievementReader, AchievementReader>();
        services.AddScoped<IAchievementService, AchievementService>();

        services.AddScoped<IExerciseRepository, ExerciseRepository>();
        services.AddScoped<IRoutineRepository, RoutineRepository>();
        services.AddScoped<IRoutineLogRepository, RoutineLogRepository>();
        services.AddScoped<IRoutineReader, RoutineReader>();

        services.AddScoped<GSM.Application.Classes.Abstractions.IClassRepository, GSM.Infrastructure.Classes.ClassRepository>();
        services.AddScoped<GSM.Application.Classes.Abstractions.IClassSessionRepository, GSM.Infrastructure.Classes.ClassSessionRepository>();
        services.AddScoped<GSM.Application.Classes.Abstractions.IClassBookingRepository, GSM.Infrastructure.Classes.ClassBookingRepository>();
        services.AddScoped<GSM.Application.Classes.Public.IClassReader, GSM.Infrastructure.Classes.ClassReader>();

        services.AddScoped<IGymAdministration, GymAdministration>();
        services.AddScoped<GSM.Application.SuperAdmin.Abstractions.IGymSubscriptionRepository, GSM.Infrastructure.SuperAdmin.GymSubscriptionRepository>();
        services.AddScoped<GSM.Application.SuperAdmin.Abstractions.ISubscriptionPaymentRepository, GSM.Infrastructure.SuperAdmin.SubscriptionPaymentRepository>();
        services.AddScoped<GSM.Application.SuperAdmin.Public.ISubscriptionReader, GSM.Infrastructure.SuperAdmin.SubscriptionReader>();

        return services;
    }

    private static void AddFileStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<R2Options>().Bind(configuration.GetSection(R2Options.SectionName));

        var r2 = configuration.GetSection(R2Options.SectionName).Get<R2Options>() ?? new R2Options();
        if (r2.IsConfigured)
        {
            services.AddSingleton<IFileStorage, R2FileStorage>();
        }
        else
        {
            services.AddSingleton<IFileStorage, DisabledFileStorage>();
        }
    }

    private static void AddEmail(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ResendOptions>().Bind(configuration.GetSection(ResendOptions.SectionName));

        var resend = configuration.GetSection(ResendOptions.SectionName).Get<ResendOptions>() ?? new ResendOptions();
        if (resend.IsConfigured)
        {
            services.AddHttpClient<IEmailSender, ResendEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
    }
}
