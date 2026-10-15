using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Compone el <see cref="SubscriptionDetail"/> de un gym: la suscripción más el plan del SaaS, el límite de
/// miembros efectivo y cuántos miembros cuentan hoy. Lo comparten todos los casos de uso que devuelven la ficha.
/// </summary>
public sealed class SubscriptionDetailBuilder(
    IClock clock,
    ISaasPlanRepository plans,
    IMembershipReader memberships)
{
    public async Task<SubscriptionDetail> BuildAsync(
        GymSubscription sub, GymStatus manualStatus, CancellationToken cancellationToken)
    {
        var limit = await plans.GetGymLimitAsync(sub.GymId, cancellationToken);
        var counted = await memberships.GetCountedMemberCountAsync(sub.GymId, cancellationToken);
        return SubscriptionMapper.ToDetail(sub, manualStatus, clock.Today, limit, counted);
    }
}
