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
/// Levanta la API real contra la base de datos local <c>gsm_test</c>. Entorno "Testing":
/// el seeder de desarrollo no corre. Sustituye <see cref="ICurrentUserService"/> por
/// <see cref="MutableCurrentUser"/> para poder fijar la identidad en cada test.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private const string TestConnectionString =
        "Host=localhost;Port=5432;Database=gsm_test;Username=gsm;Password=gsm";

    private bool _migrated;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", TestConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-32-bytes-min!!");
        builder.UseSetting("Jwt:Issuer", "gsm-api");
        builder.UseSetting("Jwt:Audience", "gsm-clients");

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

            services.AddSingleton<RoutineEventRecorder>();
            services.AddScoped<GSM.Application.Common.Events.IDomainEventHandler<
                GSM.Application.Routines.Events.RoutineSessionLogged>>(
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
