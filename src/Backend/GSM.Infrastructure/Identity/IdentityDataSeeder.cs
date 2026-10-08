using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Siembra idempotente del superadmin bootstrap. Pensada para llamarse al arrancar en Development.
/// En la etapa 2 se amplía con el gym demo, su owner y su staff.
/// </summary>
public static class IdentityDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IOptions<IdentitySeedOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentityDataSeeder));

        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
        {
            logger.LogInformation("Seed de superadmin omitido: no hay 'Seed:SuperAdmin' configurado.");
            return;
        }

        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        if (await userManager.FindByEmailAsync(options.Email) is not null)
        {
            return;
        }

        var superAdmin = new AppUser
        {
            UserName = options.Email,
            Email = options.Email,
            EmailConfirmed = true,
            FullName = options.FullName,
            IsSuperAdmin = true,
            IsActive = true,
        };

        var result = await userManager.CreateAsync(superAdmin, options.Password);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "No se pudo sembrar el superadmin: " +
                string.Join("; ", result.Errors.Select(e => $"{e.Code} {e.Description}")));
        }

        logger.LogInformation("Superadmin bootstrap creado: {Email}", options.Email);
    }
}
