using GSM.Application.Common.Abstractions;
using GSM.Infrastructure.Common;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GSM.IntegrationTests;

/// <summary>
/// Levanta la API real contra la base de datos de tests (<c>GSM_TEST_DATABASE</c>, por defecto <c>gsm_test</c>). Entorno "Testing":
/// el seeder de desarrollo no corre. Sustituye <see cref="ICurrentUserService"/> por
/// <see cref="MutableCurrentUser"/> para poder fijar la identidad en cada test.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Base de datos de los tests: variable de entorno <c>GSM_TEST_DATABASE</c> (una por carril de
    /// trabajo, ver <c>scripts/new-lane.ps1</c>) o <c>gsm_test</c> por defecto.
    /// </summary>
    public static string TestDatabaseName { get; } =
        Environment.GetEnvironmentVariable("GSM_TEST_DATABASE") is { Length: > 0 } name ? name : "gsm_test";

    private static readonly string TestConnectionString =
        $"Host=localhost;Port=5432;Database={TestDatabaseName};Username=gsm;Password=gsm";

    private bool _migrated;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", TestConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-32-bytes-min!!");
        builder.UseSetting("Jwt:Issuer", "gsm-api");
        builder.UseSetting("Jwt:Audience", "gsm-clients");
        builder.UseSetting("Checkin:SigningKey", "integration-tests-checkin-key-32-bytes-min!!");
        builder.UseSetting("Communications:UnsubscribeSigningKey", "integration-tests-unsubscribe-key-32-bytes!!");
        builder.UseSetting("Communications:PublicApiBaseUrl", "https://api.test.local");
        // El dispatcher de avisos no corre solo en los tests: cada test ejecuta una vuelta a mano.
        builder.UseSetting("Notifications:Dispatcher:Enabled", "false");
        builder.UseSetting("Jobs:Secret", "integration-tests-jobs-secret-32-bytes-min!!");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<MutableCurrentUser>();
            services.AddSingleton<ICurrentUserService>(sp => sp.GetRequiredService<MutableCurrentUser>());

            services.RemoveAll<IClock>();
            services.AddSingleton<SystemClock>();
            services.AddSingleton<MutableClock>();
            services.AddSingleton<IClock>(sp => sp.GetRequiredService<MutableClock>());

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<CapturingEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());

            services.RemoveAll<GSM.Application.Notifications.Abstractions.IPushSender>();
            services.AddSingleton<CapturingPushSender>();
            services.AddSingleton<GSM.Application.Notifications.Abstractions.IPushSender>(
                sp => sp.GetRequiredService<CapturingPushSender>());

            services.AddSingleton<RoutineEventRecorder>();
            services.AddScoped<GSM.Application.Common.Events.IDomainEventHandler<
                GSM.Application.Routines.Events.RoutineSessionLogged>>(
                sp => sp.GetRequiredService<RoutineEventRecorder>());
            services.AddScoped<GSM.Application.Common.Events.IDomainEventHandler<
                GSM.Application.Routines.Events.PersonalRecordSet>>(
                sp => sp.GetRequiredService<RoutineEventRecorder>());
        });
    }

    public IServiceScope CreateScopeWithMigration()
    {
        var scope = Services.CreateScope();

        if (!_migrated)
        {
            scope.ServiceProvider.GetRequiredService<GsmDbContext>().Database.Migrate();
            _migrated = true;
        }

        return scope;
    }
}
