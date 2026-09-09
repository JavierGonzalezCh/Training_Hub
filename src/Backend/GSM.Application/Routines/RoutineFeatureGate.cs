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

    /// <summary><c>members_can_create_routines</c>: el auto-servicio del socio.</summary>
    public static async Task EnsureSelfServiceEnabledAsync(
        IGymReader gyms, Guid gymId, CancellationToken cancellationToken)
    {
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
        if (flags is null || !flags.MembersCanCreateRoutines)
        {
            throw new ForbiddenDomainException(
                "Tu gimnasio no permite que los socios se armen rutinas.");
        }
    }
}
