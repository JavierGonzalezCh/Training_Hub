using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Proyección entidad → DTO del módulo. No usa Mapperly: el estado efectivo y los días hasta la
/// inhabilitación son campos calculados que dependen del día de hoy y del estado manual del gym.
/// </summary>
internal static class SubscriptionMapper
{
    public static SubscriptionDetail ToDetail(
        GymSubscription sub, GymStatus manualStatus, DateOnly today, GymMemberLimit? limit, int countedMembers)
        => new(
            Plan: limit?.PlanName ?? sub.Plan.ToString(),
            SaasPlanId: limit?.PlanId,
            MemberLimit: limit?.Effective,
            MemberLimitOverride: limit?.Override,
            CountedMembers: countedMembers,
            MonthlyPrice: sub.MonthlyPrice,
            StartedOn: sub.StartedOn,
            TrialEndsOn: sub.TrialEndsOn,
            PaidThrough: sub.PaidThrough,
            EffectiveStatus: sub.DeriveStatus(manualStatus, today).ToString(),
            ManualStatus: manualStatus.ToString(),
            DaysUntilDisabled: sub.DaysUntilDisabled(manualStatus, today),
            Notes: sub.Notes,
            TrialGraceDays: sub.TrialGraceDays);

    public static SubscriptionPaymentItem ToItem(SubscriptionPayment p)
        => new(p.Id, p.GymId, p.Amount, p.Method.ToString(), p.CoversFrom, p.CoversThrough,
            p.PaidAt, p.RegisteredBy, p.Reference, p.Notes);

    public static SubscriptionPaymentItem ToItem(SubscriptionPaymentRow p)
        => new(p.Id, p.GymId, p.Amount, p.Method.ToString(), p.CoversFrom, p.CoversThrough,
            p.PaidAt, p.RegisteredBy, p.Reference, p.Notes);

    public static PlatformPaymentItem ToItem(PlatformPaymentRow p)
        => new(p.Id, p.GymId, p.GymName, p.Amount, p.Method.ToString(), p.CoversFrom, p.CoversThrough,
            p.PaidAt, p.RegisteredBy);
}
