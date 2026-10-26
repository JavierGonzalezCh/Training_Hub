using GSM.Application.Common.Events;
using GSM.Domain.Payments;

namespace GSM.Application.Gyms.Events;

/// <summary>
/// Se publica tras crear un gym (y su owner). Achievements lo consume para sembrar logros y
/// SuperAdmin para dar de alta su suscripción.
/// </summary>
/// <param name="SaasPlanId">Plan del SaaS que eligió el SuperAdmin (<c>superadmin.saas_plans</c>); sin FK, lo valida SuperAdmin.</param>
/// <param name="TrialDays">Días de prueba que eligió el SuperAdmin; <c>null</c> si no hay prueba.</param>
/// <param name="FirstPayment">Primer pago ya resuelto (fechas incluidas) cuando no hay prueba.</param>
/// <param name="MonthlyPrice">Precio mensual negociado; 0 si aún no se fija.</param>
/// <param name="CreatedBy">El SuperAdmin que dio de alta el gym; firma el primer pago.</param>
/// <remarks>
/// Sin <paramref name="TrialDays"/> ni <paramref name="FirstPayment"/> (un evento antiguo) el handler de
/// SuperAdmin aplica la prueba de 30 días de siempre, para que un reintento de un evento viejo no falle.
/// </remarks>
public sealed record GymCreated(
    Guid GymId,
    string Name,
    Guid? SaasPlanId = null,
    int? TrialDays = null,
    GymFirstPayment? FirstPayment = null,
    decimal MonthlyPrice = 0m,
    Guid? CreatedBy = null,
    GSM.Domain.Gyms.GymType Type = GSM.Domain.Gyms.GymType.Gym) : IDomainEvent;

public sealed record GymFirstPayment(
    decimal Amount,
    PaymentMethod Method,
    DateOnly CoversFrom,
    DateOnly CoversThrough,
    string? Reference,
    string? Notes);
