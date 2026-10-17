using GSM.Application.Gyms;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Identity;

namespace GSM.API.Seed;

/// <summary>
/// Datos de desarrollo. Idempotente: superadmin bootstrap + un gym demo con owner y staff.
/// Se ejecuta solo en Development, al arrancar.
/// </summary>
public static class DevSeeder
{
    private const string DemoSlug = "demo";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await IdentityDataSeeder.SeedAsync(services, cancellationToken);
        await SeedDemoGymAsync(services, cancellationToken);
    }

    private static async Task SeedDemoGymAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var gyms = services.GetRequiredService<IGymRepository>();

        if (await gyms.SlugExistsAsync(DemoSlug, cancellationToken))
        {
            return;
        }

        var createGym = services.GetRequiredService<CreateGymHandler>();
        var provisioning = services.GetRequiredService<IUserProvisioning>();

        var result = await createGym.HandleWithOwnerPasswordAsync(
            new CreateGymRequest(
                Name: "Gimnasio Demo",
                Slug: DemoSlug,
                Nit: "900123456-7",
                Phone: "3000000000",
                Email: "contacto@demo.local",
                Owner: new CreateGymOwner("owner@demo.local", "Demo Owner"),
                TrialDays: 30),
            "ChangeMe_123456",
            cancellationToken);

        await provisioning.CreateGymUserAsync(
            "staff@demo.local", "Demo Staff", "ChangeMe_123456",
            UserRole.Staff, result.GymId, cancellationToken);
    }
}
