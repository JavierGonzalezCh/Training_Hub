using GSM.Domain.Gyms;

namespace GSM.Application.Gyms.Abstractions;

/// <summary>Acceso de escritura/consulta a las entidades del módulo Gyms. Sin <c>SaveChanges</c>.</summary>
public interface IGymRepository
{
    Task<Gym?> GetByIdAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    Task<GymFeatureFlags?> GetFeatureFlagsAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Estado del onboarding del gym; <c>null</c> si nunca se tocó (gym nuevo, pendiente).</summary>
    Task<GymOnboarding?> GetOnboardingAsync(Guid gymId, CancellationToken cancellationToken = default);

    void Add(GymOnboarding onboarding);

    void Add(Gym gym);

    void Add(GymFeatureFlags featureFlags);
}
