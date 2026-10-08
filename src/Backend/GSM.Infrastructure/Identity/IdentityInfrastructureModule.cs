using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Identity;

public static class IdentityInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Identity.</summary>
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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
        services.AddScoped<IGymAccessService, GymAccessService>();
        services.AddScoped<IIdentityReader, IdentityReader>();
        services.AddScoped<IUserProvisioning, UserProvisioning>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IStaffAccountService, StaffAccountService>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
