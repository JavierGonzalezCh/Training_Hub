using GSM.Application.Gyms.Public;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Classes;

/// <summary>
/// Con <c>show_classes = false</c> los endpoints del miembro responden 403; los de staff siguen
/// abiertos (para preparar clases antes de encender el módulo).
/// </summary>
internal static class ClassFeatureGate
{
    public static async Task EnsureMemberModuleEnabledAsync(
        IGymReader gyms, Guid gymId, CancellationToken cancellationToken)
    {
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
        if (flags is null || !flags.ShowClasses)
        {
            throw new ForbiddenDomainException(
                "El módulo de clases no está habilitado para este gimnasio.");
        }
    }
}
