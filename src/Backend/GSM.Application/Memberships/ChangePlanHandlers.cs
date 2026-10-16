using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Events;
using GSM.Application.Payments.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Memberships;

/// <summary>Todo lo que se calcula de un cambio de plan antes de confirmarlo (vista previa y confirmación comparten esto).</summary>
public sealed record PlanChangeQuote(
    Membership Current,
    string CurrentPlanName,
    MembershipPlan NewPlan,
    PlanChangeCredit Credit,
    decimal CreditValue,
    string? CreditAdjustmentReason,
    MembershipPricing Pricing,
    decimal CreditApplied,
    decimal RefundAmount,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>
/// Calcula el cambio de plan de la membresía vigente (Memberships v2 §8): el crédito de lo no usado, el precio de la
/// nueva, lo que se cobra o se devuelve. La comparten la vista previa y la confirmación, para que lo que se
/// muestra sea exactamente lo que se crea.
/// </summary>
public sealed class PlanChangeResolver(
    IClock clock,
    IMemberReader members,
    IMembershipRepository memberships,
    IPaymentReader payments)
{
    public async Task<PlanChangeQuote> ResolveAsync(
        Guid memberId, Guid membershipId, ChangePlanRequest request, CancellationToken cancellationToken)
    {
        var today = clock.Today;

        var member = await members.GetAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");
        if (member.Status != MemberStatus.Active)
        {
            throw new ConflictException("No se puede cambiar el plan de un miembro inactivo.");
        }

        var current = await MembershipLookup.RequireAsync(memberships, memberId, membershipId, cancellationToken);
        if (!current.CoversDate(today))
        {
            throw new ConflictException("Solo se puede cambiar el plan de la membresía que está vigente hoy.");
        }

        var newPlan = await memberships.GetPlanByIdAsync(request.PlanId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");
        if (!newPlan.IsActive)
        {
            throw new ConflictException("El plan está inactivo.");
        }

        // La nueva empieza hoy: si hay membresías encadenadas detrás de la vigente quedarían con un hueco o
        // solapadas. Se pide resolverlas antes (cancelarlas o cambiarlas) en vez de moverlas en silencio.
        var following = await memberships.GetActiveStartingAfterAsync(memberId, today, cancellationToken);
        if (following.Count > 0)
        {
            throw new ConflictException(
                "El miembro tiene membresías programadas después de la vigente. Cancélalas antes de cambiar el plan.");
        }

        var freezes = await memberships.GetFreezesAsync([current.Id], cancellationToken);
        var used = await MembershipLookup.UsedAsync(memberships, current, cancellationToken) ?? 0;
        var netPaid = await payments.GetNetPaidForMembershipAsync(current.Id, cancellationToken);

        var credit = PlanChangeCredit.Compute(current, freezes, used, netPaid, today);
        var (creditValue, adjustReason) = credit.Resolve(request.CreditOverride, request.CreditAdjustmentReason);

        // Un cambio de plan nunca cobra matrícula: el miembro ya tiene historial en el gym.
        var pricing = MembershipPricing.Resolve(
            newPlan.Price, request.AgreedPrice, request.DiscountReason, 0m, false, false, null);

        var total = pricing.AgreedPrice + pricing.EnrollmentFee;
        var applied = Math.Min(creditValue, total);
        var refund = creditValue - applied;

        var end = MembershipCalendar.Resolve(today, newPlan.DurationUnit, newPlan.DurationValue, null).EndDate;
        var currentPlanName = await MembershipLookup.PlanNameAsync(memberships, current, cancellationToken);
        return new PlanChangeQuote(
            current, currentPlanName, newPlan, credit, creditValue, adjustReason, pricing, applied, refund, today, end);
    }
}

/// <summary>Vista previa del cambio de plan: el crédito, la membresía nueva y qué se cobra o se devuelve. No escribe.</summary>
public sealed class PreviewPlanChangeHandler(ICurrentUserService currentUser, PlanChangeResolver resolver)
{
    public async Task<PlanChangePreviewResponse> HandleAsync(
        Guid memberId, Guid membershipId, ChangePlanRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var quote = await resolver.ResolveAsync(memberId, membershipId, request, cancellationToken);
        return new PlanChangePreviewResponse(
            quote.Current.Id,
            quote.CurrentPlanName,
            quote.Current.EndDate,
            quote.Credit.NetPaid,
            quote.Credit.Base,
            quote.Credit.Calculated,
            quote.CreditValue,
            quote.CreditApplied,
            quote.Credit.DaysLeft,
            quote.Credit.DaysTotal,
            quote.Credit.EntriesLeft,
            quote.Credit.EntriesTotal,
            quote.NewPlan.Id,
            quote.NewPlan.Name,
            quote.NewPlan.Price,
            quote.Pricing.AgreedPrice,
            quote.StartDate,
            quote.EndDate,
            quote.Pricing.Total - quote.CreditApplied,
            quote.RefundAmount);
    }
}

/// <summary>
/// Cambia el plan de la membresía vigente (§8): la cancela hoy con el motivo "Cambio de plan" y crea la nueva, que
/// empieza hoy y lleva el crédito de lo no usado, todo en una sola transacción de Memberships. Cobrar la diferencia o
/// devolver el excedente es de Payments: lo hace la UI con una segunda llamada, como al asignar. Los pagos de la
/// anterior no se anulan.
/// </summary>
public sealed class ChangePlanHandler(
    ICurrentUserService currentUser,
    IClock clock,
    PlanChangeResolver resolver,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<ChangePlanHandler> logger)
{
    public const string CancellationReason = "Cambio de plan";

    public async Task<ChangePlanResponse> HandleAsync(
        Guid memberId, Guid membershipId, ChangePlanRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;
        var quote = await resolver.ResolveAsync(memberId, membershipId, request, cancellationToken);

        var plan = quote.NewPlan;
        quote.Current.Cancel(CancellationReason, clock.UtcNow);
        var next = Membership.Create(
            gymId, memberId, plan.Id, quote.StartDate, plan.DurationValue,
            RenewalKind.New, null, quote.Pricing, plan.DurationUnit, null, plan.Entries,
            quote.CreditApplied, quote.CreditAdjustmentReason, quote.Current.Id);
        memberships.Add(next);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Cambio de plan del miembro {MemberId}: {PreviousId} -> {MembershipId}, crédito {Credit}, cobrar {Total}, devolver {Refund}",
            memberId, quote.Current.Id, next.Id, quote.CreditApplied, next.TotalToCharge, quote.RefundAmount);

        await events.PublishAsync(new MembershipCancelled(quote.Current.Id, memberId, gymId), cancellationToken);
        await events.PublishAsync(new MembershipCreated(next.Id, memberId, gymId, plan.Id), cancellationToken);
        await events.PublishAsync(
            new MembershipPlanChanged(
                next.Id, quote.Current.Id, memberId, gymId, plan.Id, quote.CreditApplied, quote.RefundAmount),
            cancellationToken);

        var freezes = await memberships.GetFreezesAsync([quote.Current.Id], cancellationToken);
        var previous = MembershipMapper.ToResponse(
            quote.Current, quote.CurrentPlanName, today, freezes,
            await MembershipLookup.UsedAsync(memberships, quote.Current, cancellationToken));
        var current = MembershipMapper.ToResponse(next, plan.Name, today);
        return new ChangePlanResponse(previous, current, quote.CreditApplied, next.TotalToCharge, quote.RefundAmount);
    }
}
