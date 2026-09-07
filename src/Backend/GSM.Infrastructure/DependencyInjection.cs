using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Gyms;
using GSM.Infrastructure.Identity;
using GSM.Infrastructure.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                })
                .UseSnakeCaseNamingConvention());

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
            .AddEntityFrameworkStores<GsmDbContext>();

        services.AddScoped<IUserCredentialsService, UserCredentialsService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IIdentityReader, IdentityReader>();
        services.AddScoped<IUserProvisioning, UserProvisioning>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddScoped<IGymRepository, GymRepository>();
        services.AddScoped<IGymReader, GymReader>();

        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IMemberReader, MemberReader>();

        return services;
    }
}
