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

                // Bloqueo por intentos (§7). El conteo lo hace UserCredentialsService con IClock.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = LockoutPolicy.MaxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan = LockoutPolicy.Duration;

                // Los enlaces de restablecer (24 h) y activar (72 h) usan sus propios proveedores con IClock.
                options.Tokens.ProviderMap[ResetTokenProvider.ProviderName] =
                    new TokenProviderDescriptor(typeof(ResetTokenProvider));
                options.Tokens.ProviderMap[ActivationTokenProvider.ProviderName] =
                    new TokenProviderDescriptor(typeof(ActivationTokenProvider));
                options.Tokens.PasswordResetTokenProvider = ResetTokenProvider.ProviderName;
            })
            .AddEntityFrameworkStores<GsmDbContext>()
            .AddDefaultTokenProviders();

        services.AddOptions<AccountLinkOptions>().Bind(configuration.GetSection(AccountLinkOptions.SectionName));
        services.AddTransient<ResetTokenProvider>();
        services.AddTransient<ActivationTokenProvider>();
        services.AddSingleton<IAccountLinkBuilder, AccountLinkBuilder>();
        services.AddSingleton<IMemberRegistrationLinks>(sp => (AccountLinkBuilder)sp.GetRequiredService<IAccountLinkBuilder>());
        services.AddScoped<IAccountLinkService, AccountLinkService>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
        services.AddScoped<IUserCredentialsService, UserCredentialsService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IGymAccessService, GymAccessService>();
        services.AddScoped<IMemberAccessRemoval, MemberAccessRemoval>();
        services.AddScoped<IKioskDeviceService, KioskDeviceService>();
        services.AddScoped<ILoginCodeService, LoginCodeService>();
        services.AddScoped<IEmailChangeService, EmailChangeService>();
        services.AddScoped<IIdentityReader, IdentityReader>();
        services.AddScoped<IUserProvisioning, UserProvisioning>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IStaffAccountService, StaffAccountService>();
        services.AddScoped<IOwnerAccountService, OwnerAccountService>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
