using GSM.Application.Gyms.Public;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Routines;

/// <summary>
/// Con <c>show_routines = false</c> los endpoints del socio responden 403; los de staff siguen
/// abiertos (para preparar rutinas antes de encender el módulo).
/// </summary>
internal static class RoutineFeatureGate
{
    public static async Task EnsureMemberModuleEnabledAsync(
        IGymReader gyms, Guid gymId, CancellationToken cancellationToken)
    {
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
        if (flags is null || !flags.ShowRoutines)
        {
            throw new ForbiddenDomainException(
                "El módulo de rutinas no está habilitado para este gimnasio.");
        }
    }
}
