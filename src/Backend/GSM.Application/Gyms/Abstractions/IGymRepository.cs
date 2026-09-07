using GSM.Domain.Gyms;

namespace GSM.Application.Gyms.Abstractions;

/// <summary>Acceso de escritura/consulta a las entidades del módulo Gyms. Sin <c>SaveChanges</c>.</summary>
public interface IGymRepository
{
    Task<Gym?> GetByIdAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    Task<GymFeatureFlags?> GetFeatureFlagsAsync(Guid gymId, CancellationToken cancellationToken = default);

    void Add(Gym gym);

    void Add(GymFeatureFlags featureFlags);
}
