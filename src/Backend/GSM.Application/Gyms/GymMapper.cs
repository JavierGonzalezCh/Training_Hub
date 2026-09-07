using GSM.Application.Gyms.Contracts;
using GSM.Application.Gyms.Public;
using GSM.Domain.Gyms;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Gyms;

/// <summary>Mapeo entidad → DTO del módulo Gyms (Mapperly, generado en compilación).</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class GymMapper
{
    public static partial GymResponse ToResponse(Gym gym);

    public static partial GymFeatureFlagsResponse ToResponse(GymFeatureFlags flags);

    public static partial GymFeatureFlagsView ToView(GymFeatureFlags flags);
}
