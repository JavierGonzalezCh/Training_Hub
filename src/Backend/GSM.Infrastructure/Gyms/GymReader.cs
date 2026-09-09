using GSM.Application.Gyms.Public;
using GSM.Domain.Gyms;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Gyms;

/// <summary>
/// Lectura cruzada por id explícito: ignora el Global Query Filter a propósito para que el
/// contrato sea predecible sin importar el contexto del llamador.
/// </summary>
public sealed class GymReader(GsmDbContext dbContext) : IGymReader
{
    public Task<bool> ExistsAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<Gym>().IgnoreQueryFilters()
            .AnyAsync(g => g.Id == gymId, cancellationToken);

    public Task<GymBranding?> GetBrandingAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<Gym>().IgnoreQueryFilters()
            .Where(g => g.Id == gymId)
            .Select(g => new GymBranding(g.Name, g.LogoUrl, g.PrimaryColor, g.SecondaryColor))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<GymFeatureFlagsView?> GetFeatureFlagsAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<GymFeatureFlags>().IgnoreQueryFilters()
            .Where(f => f.GymId == gymId)
            .Select(f => new GymFeatureFlagsView(
                f.ShowRoutines, f.ShowClasses, f.ShowProgress, f.ShowGamification,
                f.ShowCommunity, f.CheckinQr, f.CommunityComments, f.MembersCanCreateRoutines))
            .FirstOrDefaultAsync(cancellationToken);
}
