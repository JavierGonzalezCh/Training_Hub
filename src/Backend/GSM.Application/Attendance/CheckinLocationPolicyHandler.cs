using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Domain.Attendance;

namespace GSM.Application.Attendance;

/// <summary>
/// ¿La app debe pedir la ubicación antes de registrar la entrada? Solo si el gym usa el QR fijo y configuró sus
/// coordenadas (A6). Así el QR rotativo nunca dispara el permiso de ubicación del teléfono.
/// </summary>
public sealed class GetCheckinLocationPolicyHandler(
    ICurrentUserService currentUser, ICheckinSettingsRepository settings, IGymReader gyms)
{
    public async Task<CheckinLocationPolicyResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await settings.GetAsync(gymId, cancellationToken) ?? CheckinSettings.Default(gymId);
        if (current.Mode != CheckinMode.Static)
        {
            return new CheckinLocationPolicyResponse(false);
        }

        return new CheckinLocationPolicyResponse(await gyms.GetGeofenceAsync(gymId, cancellationToken) is not null);
    }
}
