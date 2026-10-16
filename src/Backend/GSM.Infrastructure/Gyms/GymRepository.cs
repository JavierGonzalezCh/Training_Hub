using GSM.Application.Gyms.Abstractions;
using GSM.Domain.Gyms;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Gyms;

public sealed class GymRepository(GsmDbContext dbContext) : IGymRepository
{
    public Task<Gym?> GetByIdAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<Gym>().FirstOrDefaultAsync(g => g.Id == gymId, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
        => dbContext.Set<Gym>().IgnoreQueryFilters().AnyAsync(g => g.Slug == slug, cancellationToken);

    public Task<GymFeatureFlags?> GetFeatureFlagsAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<GymFeatureFlags>().FirstOrDefaultAsync(f => f.GymId == gymId, cancellationToken);

    public Task<GymOnboarding?> GetOnboardingAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<GymOnboarding>().FirstOrDefaultAsync(o => o.GymId == gymId, cancellationToken);

    public void Add(GymOnboarding onboarding) => dbContext.Set<GymOnboarding>().Add(onboarding);

    public void Add(Gym gym) => dbContext.Set<Gym>().Add(gym);

    public void Add(GymFeatureFlags featureFlags) => dbContext.Set<GymFeatureFlags>().Add(featureFlags);
}
