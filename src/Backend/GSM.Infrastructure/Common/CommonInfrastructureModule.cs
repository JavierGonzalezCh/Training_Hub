using GSM.Application.Common.Abstractions;
using GSM.Infrastructure.Email;
using GSM.Infrastructure.Persistence;
using GSM.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Common;

public static class CommonInfrastructureRegistration
{
    /// <summary>Lo genuinamente transversal: DbContext, reloj, unidad de trabajo, almacenamiento y correo.</summary>
    public static IServiceCollection AddCommonInfrastructure(
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
                    PgEnumMappings.ConfigureProvider(npgsql);
                })
                .UseSnakeCaseNamingConvention());

        services.AddOptions<ClockOptions>().Bind(configuration.GetSection(ClockOptions.SectionName));
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        AddFileStorage(services, configuration);
        AddEmail(services, configuration);

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
