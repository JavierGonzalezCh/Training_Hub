using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.SuperAdmin;

/// <summary>Ficha del cliente: datos del gym y su suscripción con el estado efectivo de hoy.</summary>
public sealed class GetGymDetailHandler(
    IGymReader gyms,
    IGymSubscriptionRepository subscriptions,
    SubscriptionDetailBuilder detailBuilder,
    GSM.Application.Attendance.Public.IAttendanceReader attendance,
    GSM.Application.Memberships.Public.IMembershipReader memberships,
    GSM.Application.Routines.Public.IRoutineReader routines,
    GSM.Application.Achievements.Public.IAchievementReader achievements)
{
    public async Task<GymDetailResponse> HandleAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var gym = await gyms.GetAdminViewAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var sub = await subscriptions.GetByGymIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no tiene suscripción.");

        return new GymDetailResponse(
            gym.Id,
            gym.Name,
            gym.Slug,
            gym.Nit,
            gym.Phone,
            gym.Email,
            await detailBuilder.BuildAsync(sub, gym.ManualStatus, cancellationToken),
            gym.Type.ToString(),
            new GymTemplateStatus(
                await attendance.HasCheckinSettingsAsync(gymId, cancellationToken),
                await memberships.HasSettingsAsync(gymId, cancellationToken),
                await routines.HasSettingsAsync(gymId, cancellationToken),
                await achievements.HasAchievementsAsync(gymId, cancellationToken)));
    }
}
