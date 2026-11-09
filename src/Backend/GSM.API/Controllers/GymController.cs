using GSM.API.Auth;
using GSM.API.Uploads;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.SuperAdmin.Public;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Gestión del gym del usuario autenticado (sin id en la ruta: siempre el del token).</summary>
[ApiController]
[Route("api/gym")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class GymController : ControllerBase
{
    /// <summary>
    /// Datos del gym más el bloque <c>subscription</c>, que se compone aquí y no en el caso de uso
    /// para que Gyms no dependa de SuperAdmin (mismo patrón que <c>GET /api/auth/me</c>).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<GymResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymResponse>> Get(
        [FromServices] GetGymHandler handler,
        [FromServices] ISubscriptionReader subscriptions,
        CancellationToken cancellationToken)
    {
        var gym = await handler.HandleAsync(cancellationToken);
        var view = await subscriptions.GetStatusViewAsync(gym.Id, cancellationToken);
        return Ok(gym with { Subscription = view?.ToInfo() });
    }

    /// <summary>
    /// La configuración que la plantilla recomienda para el tipo de negocio del gym (módulos, entrada, membresías y
    /// rutinas), para comparar con la actual y aplicar lo que el Owner marque. Se compone aquí con las plantillas de cada
    /// módulo; no escribe nada: cada cambio se guarda por el endpoint normal de su módulo.
    /// </summary>
    [HttpGet("recommended-configuration")]
    [EndpointName("GetRecommendedConfiguration")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<RecommendedConfiguration>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RecommendedConfiguration>> GetRecommendedConfiguration(
        [FromServices] GetGymHandler handler, CancellationToken cancellationToken)
    {
        var gym = await handler.HandleAsync(cancellationToken);
        var type = Enum.TryParse<GSM.Domain.Gyms.GymType>(gym.Type, ignoreCase: true, out var parsed)
            ? parsed
            : GSM.Domain.Gyms.GymType.Gym;

        var checkin = GSM.Application.Attendance.CheckinTemplates.For(type);
        var memberships = GSM.Application.Memberships.MembershipTemplates.For(type);
        var routines = GSM.Application.Routines.RoutineSettingsTemplates.For(type);

        return Ok(new RecommendedConfiguration(
            type.ToString(),
            GymMapper.ToResponse(GSM.Domain.Gyms.GymFeatureFlags.ForType(gym.Id, type)),
            new RecommendedCheckin(
                checkin.Mode.ToString(), checkin.AssumedStayMinutes, checkin.RemoteTraining),
            new RecommendedMemberships(memberships.GraceDays, memberships.ExpiringDays, memberships.EnrollmentFee),
            new RecommendedRoutines(routines.ReviewWeeks)));
    }

    /// <summary>Uso del límite de miembros del plan del SaaS ("142 / 150"). <c>limit</c> nulo = sin límite.</summary>
    [HttpGet("member-usage")]
    [ProducesResponseType<GSM.Application.SuperAdmin.Contracts.MemberUsageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GSM.Application.SuperAdmin.Contracts.MemberUsageResponse>> GetMemberUsage(
        [FromServices] GSM.Application.SuperAdmin.GetMemberUsageHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// Suscripción del gym al SaaS, solo lectura para el Owner (S3-3): plan, precio, estado de hoy, hasta cuándo está
    /// cubierta, límite de miembros y últimos pagos. El gym no paga desde el sistema. 404 si aún no tiene suscripción.
    /// </summary>
    [HttpGet("subscription")]
    [EndpointName("OwnSubscription")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<GSM.Application.SuperAdmin.Contracts.OwnSubscriptionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GSM.Application.SuperAdmin.Contracts.OwnSubscriptionResponse>> GetOwnSubscription(
        [FromServices] GSM.Application.SuperAdmin.GetOwnSubscriptionHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<GymResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymResponse>> Update(
        UpdateGymRequest request, [FromServices] UpdateGymHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>El Owner marcado como contacto de facturación (cobros del SaaS, avisos de suscripción).</summary>
    [HttpGet("billing-contact")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<BillingContactResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BillingContactResponse>> GetBillingContact(
        [FromServices] GetBillingContactHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Marca a un Owner activo del gym como contacto de facturación. 422 si no es Owner del gym.</summary>
    [HttpPut("billing-contact")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<BillingContactResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BillingContactResponse>> SetBillingContact(
        SetBillingContactRequest request, [FromServices] SetBillingContactHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>
    /// Onboarding del gym (Gyms §5): qué pasos tienen su dato, cuál sigue y qué opcional queda para la lista
    /// "Para completar". Mientras <c>isComplete</c> sea <c>false</c>, el panel lleva todo a <c>/onboarding</c>.
    /// Lo puede leer el staff (ve "tu gym se está configurando"); lo completa el Owner con las pantallas de cada módulo.
    /// </summary>
    [HttpGet("onboarding")]
    [EndpointName("Onboarding")]
    [ProducesResponseType<OnboardingResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OnboardingResponse>> GetOnboarding(
        [FromServices] GetOnboardingHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>«Trabajo solo»: resuelve el paso de equipo del onboarding sin invitar a nadie. Solo el Owner.</summary>
    [HttpPost("onboarding/skip-team")]
    [EndpointName("SkipOnboardingTeam")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<OnboardingResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OnboardingResponse>> SkipOnboardingTeam(
        [FromServices] SkipOnboardingTeamHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpGet("feature-flags")]
    [ProducesResponseType<GymFeatureFlagsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymFeatureFlagsResponse>> GetFeatureFlags(
        [FromServices] GetFeatureFlagsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut("feature-flags")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<GymFeatureFlagsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymFeatureFlagsResponse>> UpdateFeatureFlags(
        UpdateFeatureFlagsRequest request,
        [FromServices] UpdateFeatureFlagsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    [HttpGet("opening-hours")]
    [ProducesResponseType<OpeningHoursResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OpeningHoursResponse>> GetOpeningHours(
        [FromServices] GetOpeningHoursHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Reemplaza el horario completo: los 7 días y el de festivos, cada uno con al menos un tramo.</summary>
    [HttpPut("opening-hours")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<OpeningHoursResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OpeningHoursResponse>> UpdateOpeningHours(
        UpdateOpeningHoursRequest request,
        [FromServices] UpdateOpeningHoursHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Festivos de Colombia del año con el horario que aplica a cada uno (por defecto, el año en curso).</summary>
    [HttpGet("holidays")]
    [ProducesResponseType<IReadOnlyList<HolidayResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HolidayResponse>>> ListHolidays(
        [FromQuery] int? year,
        [FromServices] ListHolidaysHandler handler,
        [FromServices] GSM.Application.Common.Abstractions.IClock clock,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(year ?? clock.Today.Year, cancellationToken));

    [HttpGet("special-days")]
    [ProducesResponseType<IReadOnlyList<SpecialDayResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SpecialDayResponse>>> ListSpecialDays(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] ListSpecialDaysHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(from, to, cancellationToken));

    /// <summary>Otro horario en una fecha, o el día completo cerrado (exige <c>confirmClosed</c>). También ajusta un festivo.</summary>
    [HttpPut("special-days/{date}")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<SpecialDayResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SpecialDayResponse>> UpsertSpecialDay(
        DateOnly date,
        UpsertSpecialDayRequest request,
        [FromServices] UpsertSpecialDayHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(date, request, cancellationToken));

    [HttpDelete("special-days/{date}")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSpecialDay(
        DateOnly date, [FromServices] DeleteSpecialDayHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(date, cancellationToken);
        return NoContent();
    }

    [HttpPost("logo")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<GymResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymResponse>> UploadLogo(
        IFormFile file, [FromServices] UploadGymLogoHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(file.ToFileToStore(), cancellationToken));
}
