using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;

namespace GSM.Domain.SuperAdmin;

/// <summary>
/// Suscripción de un gym al SaaS: plan, precio negociado con ese cliente y hasta qué día está
/// cubierto. Una por gym. Sin FK a <c>gyms.gyms</c> (otro schema): el vínculo se valida en el
/// caso de uso contra <c>IGymReader</c>.
/// </summary>
/// <remarks>
/// El estado efectivo (<see cref="EffectiveSubscriptionStatus"/>) no vive aquí como columna:
/// <see cref="DeriveStatus"/> lo calcula. <c>overdue</c> y <c>disabled</c> son puro paso del tiempo.
/// </remarks>
public sealed class GymSubscription : BaseEntity
{
    /// <summary>Días de gracia tras el vencimiento antes de pasar a <c>disabled</c>. Constante (decisión abierta 1).</summary>
    public const int GraceDays = 15;

    private GymSubscription()
    {
    }

    public Guid GymId { get; private set; }

    public GymPlan Plan { get; private set; }

    /// <summary>Precio mensual de este cliente en COP. Puede ser 0 (cortesía); nunca negativo.</summary>
    public decimal MonthlyPrice { get; private set; }

    public DateOnly StartedOn { get; private set; }

    public DateOnly? TrialEndsOn { get; private set; }

    /// <summary>Último día cubierto (inclusive). Los pagos la empujan hacia adelante.</summary>
    public DateOnly PaidThrough { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>
    /// Plan del SaaS (<c>superadmin.saas_plans</c>) que define el límite de miembros. Nulo en
    /// suscripciones creadas sin elegirlo: entonces el límite se resuelve por el nombre del
    /// <see cref="Plan"/> heredado.
    /// </summary>
    public Guid? SaasPlanId { get; private set; }

    /// <summary>Límite de miembros propio de este cliente. <c>null</c> = el del plan (SuperAdmin §V1).</summary>
    public int? MemberLimitOverride { get; private set; }

    public static GymSubscription Create(
        Guid gymId,
        GymPlan plan,
        decimal monthlyPrice,
        DateOnly startedOn,
        DateOnly paidThrough,
        DateOnly? trialEndsOn = null,
        string? notes = null)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("La suscripción necesita un gym.");
        }

        if (monthlyPrice < 0)
        {
            throw new ValidationDomainException("El precio mensual no puede ser negativo.");
        }

        if (paidThrough < startedOn)
        {
            throw new ValidationDomainException("La cobertura no puede terminar antes de que empiece la suscripción.");
        }

        return new GymSubscription
        {
            GymId = gymId,
            Plan = plan,
            MonthlyPrice = decimal.Round(monthlyPrice, 2),
            StartedOn = startedOn,
            PaidThrough = paidThrough,
            TrialEndsOn = trialEndsOn,
            Notes = Trim(notes),
        };
    }

    /// <summary>Cambia plan y precio negociado. No toca la cobertura.</summary>
    public void UpdatePlan(GymPlan plan, decimal monthlyPrice)
    {
        if (monthlyPrice < 0)
        {
            throw new ValidationDomainException("El precio mensual no puede ser negativo.");
        }

        Plan = plan;
        MonthlyPrice = decimal.Round(monthlyPrice, 2);
    }

    /// <summary>Fija el plan del SaaS que define el límite (S2).</summary>
    public void AssignSaasPlan(Guid saasPlanId)
    {
        if (saasPlanId == Guid.Empty)
        {
            throw new ValidationDomainException("El plan del SaaS no es válido.");
        }

        SaasPlanId = saasPlanId;
    }

    /// <summary>Límite propio de este cliente; <c>null</c> lo devuelve al del plan.</summary>
    public void SetMemberLimitOverride(int? limit)
    {
        if (limit is < 1)
        {
            throw new ValidationDomainException("El límite de miembros debe ser de al menos 1 (o vacío para usar el del plan).");
        }

        MemberLimitOverride = limit;
    }

    public void SetNotes(string? notes) => Notes = Trim(notes);

    /// <summary>
    /// Empuja la cobertura hasta <paramref name="coversThrough"/> si es posterior a la actual.
    /// Nunca retrocede: renovar por un pago solo suma tiempo.
    /// </summary>
    public void ExtendCoverageTo(DateOnly coversThrough)
    {
        if (coversThrough > PaidThrough)
        {
            PaidThrough = coversThrough;
        }
    }

    /// <summary>
    /// Fija la cobertura a una fecha exacta, en cualquier dirección. Solo para renovación sin pago
    /// (cortesías o corrección de fecha); deja constancia en las notas, no inventa un pago.
    /// </summary>
    public void OverrideCoverage(DateOnly paidThrough)
    {
        if (paidThrough < StartedOn)
        {
            throw new ValidationDomainException("La cobertura no puede terminar antes de que empiece la suscripción.");
        }

        PaidThrough = paidThrough;
    }

    /// <summary>
    /// Estado efectivo, método puro: sin base de datos. Recibe el estado manual del gym
    /// (<see cref="GymStatus"/>, propiedad de Gyms) y el día de negocio de hoy.
    /// </summary>
    public EffectiveSubscriptionStatus DeriveStatus(GymStatus manualStatus, DateOnly today)
        => DeriveStatus(manualStatus, today, PaidThrough, TrialEndsOn);

    /// <summary>Días que faltan para pasar a <c>disabled</c>, o <c>null</c> si no está en mora.</summary>
    public int? DaysUntilDisabled(GymStatus manualStatus, DateOnly today)
        => DaysUntilDisabled(manualStatus, today, PaidThrough, TrialEndsOn);

    /// <summary>
    /// Derivación pura sobre primitivas: sin entidad ni base de datos. Es la única definición de la
    /// regla; la sobrecarga de instancia delega aquí.
    /// </summary>
    public static EffectiveSubscriptionStatus DeriveStatus(
        GymStatus manualStatus, DateOnly today, DateOnly paidThrough, DateOnly? trialEndsOn)
    {
        if (manualStatus == GymStatus.Suspended)
        {
            return EffectiveSubscriptionStatus.Suspended;
        }

        // El trial manda solo mientras dure: sin `trial_ends_on` no hay ventana de prueba que
        // sostener, y el gym cae en la lógica de `paid_through` como cualquier otro. Tratar el
        // nulo como "trial indefinido" dejaría fuera del bloqueo a todo gym recién creado, que
        // nace con `gym_status = trial`.
        if (manualStatus == GymStatus.Trial && trialEndsOn is { } ends && today <= ends)
        {
            return EffectiveSubscriptionStatus.Trial;
        }

        if (today <= paidThrough)
        {
            return EffectiveSubscriptionStatus.Active;
        }

        if (today <= paidThrough.AddDays(GraceDays))
        {
            return EffectiveSubscriptionStatus.Overdue;
        }

        return EffectiveSubscriptionStatus.Disabled;
    }

    public static int? DaysUntilDisabled(
        GymStatus manualStatus, DateOnly today, DateOnly paidThrough, DateOnly? trialEndsOn)
        => DeriveStatus(manualStatus, today, paidThrough, trialEndsOn) == EffectiveSubscriptionStatus.Overdue
            ? paidThrough.AddDays(GraceDays).DayNumber - today.DayNumber
            : null;

    /// <summary>Día siguiente al fin de la cobertura vigente: arranque por defecto del próximo periodo.</summary>
    public DateOnly NextPeriodStart() => PaidThrough.AddDays(1);

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
