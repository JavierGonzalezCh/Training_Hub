using GSM.Application.Common;
using GSM.Domain.Gyms;
using GSM.Domain.Payments;

namespace GSM.Application.Gyms.Contracts;

/// <summary>
/// Alta de un gym por el SuperAdmin (Gyms §4, SuperAdmin §V2). El Owner <b>no lleva contraseña</b>: recibe un
/// enlace de activación. Hay que elegir <b>una</b> de dos: <c>TrialDays</c> (con prueba) o <c>FirstPayment</c>
/// (sin prueba, el gym nace activo con ese primer pago). No existe un gym sin prueba y sin pago.
/// </summary>
/// <param name="SaasPlanId">Plan del SaaS (<c>superadmin.saas_plans</c>); define el límite de miembros. Opcional.</param>
/// <param name="TrialDays">Días de prueba (1 a 365). Excluyente con <paramref name="FirstPayment"/>.</param>
/// <param name="MonthlyPrice">Precio mensual negociado con el cliente; sin indicar, 0 (se fija luego).</param>
/// <param name="FirstPayment">Primer pago del SaaS cuando no hay prueba.</param>
public sealed record CreateGymRequest(
    string Name,
    string Slug,
    string? Nit,
    string? Phone,
    string? Email,
    Guid? SaasPlanId,
    CreateGymOwner Owner,
    int? TrialDays = null,
    decimal? MonthlyPrice = null,
    CreateGymFirstPayment? FirstPayment = null,
    GymType Type = GymType.Gym);

public sealed record CreateGymOwner(string Email, string FullName);

/// <summary>
/// Primer pago del gym al SaaS (pago del SaaS, no del gym). Sin fechas, cubre desde hoy un mes.
/// </summary>
public sealed record CreateGymFirstPayment(
    decimal Amount,
    PaymentMethod Method,
    DateOnly? CoversFrom = null,
    DateOnly? CoversThrough = null,
    string? Reference = null,
    string? Notes = null);

/// <param name="OwnerInvitationSent"><c>false</c> si el correo falló: la cuenta existe y el enlace se copia desde la ficha.</param>
public sealed record CreateGymResponse(Guid GymId, Guid OwnerId, bool OwnerInvitationSent = true);

public sealed record UpdateGymRequest(
    string Name,
    string? Nit,
    string? Phone,
    string? Email,
    string? LogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    int? Capacity = null,
    string? LegalName = null,
    string? Address = null,
    string? City = null,
    double? Latitude = null,
    double? Longitude = null,
    int? GeofenceRadiusMeters = null,
    string? InstagramUrl = null,
    string? FacebookUrl = null,
    string? TikTokUrl = null,
    string? WebsiteUrl = null,
    string? WhatsAppPhone = null,
    GymType? Type = null);

/// <summary>
/// La configuración recomendada para el tipo de negocio del gym (docs/GSM-tipos-de-negocio.md §9): los mismos valores de la
/// plantilla de arranque. Solo se lee; cada cambio se guarda por el endpoint normal de su módulo.
/// </summary>
/// <param name="Type">Tipo de negocio al que corresponde.</param>
public sealed record RecommendedConfiguration(
    string Type,
    GymFeatureFlagsResponse Modules,
    RecommendedCheckin Checkin,
    RecommendedMemberships Memberships,
    RecommendedRoutines Routines);

public sealed record RecommendedCheckin(string Mode, int AssumedStayMinutes, bool RemoteTraining);

public sealed record RecommendedMemberships(int GraceDays, int ExpiringDays, decimal EnrollmentFee);

public sealed record RecommendedRoutines(int ReviewWeeks);

/// <summary>El SuperAdmin cambia el tipo de negocio de un gym (docs/GSM-tipos-de-negocio.md §9). No reaplica la plantilla.</summary>
public sealed record ChangeGymTypeRequest(GymType Type);

public sealed record UpdateFeatureFlagsRequest(
    bool ShowRoutines,
    bool ShowClasses,
    bool ShowProgress,
    bool ShowGamification,
    bool ShowCommunity,
    bool CheckinQr,
    bool MembersCanCreateRoutines,
    bool MembersCanReportEquipment,
    bool? ShowRanking = null,
    bool? ShowFrontDesk = null);

public sealed record GymResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Nit,
    string? Phone,
    string? Email,
    string? LogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    int? Capacity,
    bool IsActive,
    string Status,
    Guid OwnerId,
    DateTimeOffset CreatedAt,
    string? LegalName,
    string? Address,
    string? City,
    double? Latitude,
    double? Longitude,
    int GeofenceRadiusMeters,
    string? InstagramUrl,
    string? FacebookUrl,
    string? TikTokUrl,
    string? WebsiteUrl,
    string? WhatsAppPhone,
    SubscriptionInfo? Subscription = null,
    string Type = nameof(GymType.Gym));

/// <summary>
/// Lo que el miembro ve de su gimnasio ("Mi gimnasio"). Sin NIT, razón social ni datos comerciales.
/// El horario y la ocupación se sumarán con G3 y A5.
/// </summary>
public sealed record MemberGymResponse(
    string Name,
    string? LogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    string? Address,
    string? City,
    double? Latitude,
    double? Longitude,
    string? Phone,
    string? WhatsAppPhone,
    string? InstagramUrl,
    string? FacebookUrl,
    string? TikTokUrl,
    string? WebsiteUrl);

/// <summary>El Owner marcado como contacto de facturación del gym (cobros del SaaS, avisos de suscripción).</summary>
public sealed record BillingContactResponse(Guid? UserId, string? FullName, string? Email);

public sealed record SetBillingContactRequest(Guid UserId);

public sealed record GymFeatureFlagsResponse(
    bool ShowRoutines,
    bool ShowClasses,
    bool ShowProgress,
    bool ShowGamification,
    bool ShowCommunity,
    bool CheckinQr,
    bool MembersCanCreateRoutines,
    bool MembersCanReportEquipment,
    bool ShowRanking,
    bool ShowFrontDesk = true);
