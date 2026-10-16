using GSM.API.Auth;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Gestión de los gyms como clientes del SaaS. Opera por encima del multi-tenancy: solo el superadmin.
/// </summary>
[ApiController]
[Route("api/admin/gyms")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
public sealed class AdminGymsController : ControllerBase
{
    /// <summary>Cartera de clientes con su estado efectivo. Filtro opcional <c>?status=overdue</c>.</summary>
    [HttpGet]
    [ProducesResponseType<PortfolioResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PortfolioResponse>> Portfolio(
        [FromQuery] string? status,
        [FromServices] ListGymPortfolioHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(new PortfolioQuery { Status = status }, cancellationToken));

    /// <summary>Ficha del cliente: suscripción y estado efectivo.</summary>
    [HttpGet("{gymId:guid}")]
    [ProducesResponseType<GymDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GymDetailResponse>> Detail(
        Guid gymId, [FromServices] GetGymDetailHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, cancellationToken));

    /// <summary>Dónde va el onboarding del gym: pasos, si se omitió y con qué motivo.</summary>
    [HttpGet("{gymId:guid}/onboarding")]
    [EndpointName("AdminGymOnboarding")]
    [ProducesResponseType<GSM.Application.Gyms.Contracts.OnboardingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GSM.Application.Gyms.Contracts.OnboardingResponse>> GetOnboarding(
        Guid gymId, [FromServices] GSM.Application.Gyms.GetOnboardingHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, cancellationToken));

    /// <summary>"Omitir onboarding" (soporte): habilita el panel sin pasar por el asistente. Exige motivo; queda auditado.</summary>
    [HttpPost("{gymId:guid}/onboarding/skip")]
    [EndpointName("SkipOnboarding")]
    [ProducesResponseType<GSM.Application.Gyms.Contracts.OnboardingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GSM.Application.Gyms.Contracts.OnboardingResponse>> SkipOnboarding(
        Guid gymId,
        GSM.Application.Gyms.Contracts.SkipOnboardingRequest request,
        [FromServices] GSM.Application.Gyms.SkipOnboardingHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, request, cancellationToken));

    /// <summary>Cambia plan y precio negociado del cliente.</summary>
    [HttpPut("{gymId:guid}/subscription")]
    [ProducesResponseType<SubscriptionDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionDetail>> UpdateSubscription(
        Guid gymId,
        UpdateSubscriptionRequest request,
        [FromServices] UpdateSubscriptionHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, request, cancellationToken));

    /// <summary>Renovación sin pago (cortesías, corrección de fecha). Motivo obligatorio.</summary>
    [HttpPost("{gymId:guid}/renew")]
    [ProducesResponseType<SubscriptionDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionDetail>> Renew(
        Guid gymId,
        RenewSubscriptionRequest request,
        [FromServices] RenewSubscriptionHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, request, cancellationToken));

    /// <summary>Suspensión manual. Motivo obligatorio.</summary>
    [HttpPost("{gymId:guid}/suspend")]
    [ProducesResponseType<SubscriptionDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionDetail>> Suspend(
        Guid gymId,
        SuspendGymRequest request,
        [FromServices] SuspendGymHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, request, cancellationToken));

    /// <summary>Registrar un pago del gym al SaaS → renueva la suscripción en la misma operación.</summary>
    [HttpPost("{gymId:guid}/payments")]
    [ProducesResponseType<RegisterSubscriptionPaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RegisterSubscriptionPaymentResponse>> RegisterPayment(
        Guid gymId,
        RegisterSubscriptionPaymentRequest request,
        [FromServices] RegisterSubscriptionPaymentHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(gymId, request, cancellationToken);
        return Created((string?)null, result);
    }

    /// <summary>Histórico de pagos al SaaS del gym. Sin <c>PUT</c>/<c>DELETE</c>: los pagos son inmutables.</summary>
    [HttpGet("{gymId:guid}/payments")]
    [ProducesResponseType<IReadOnlyList<SubscriptionPaymentItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPaymentItem>>> GymPayments(
        Guid gymId, [FromServices] ListGymSubscriptionPaymentsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, cancellationToken));

    /// <summary>Saca al gym de <c>suspended</c>.</summary>
    [HttpPost("{gymId:guid}/reactivate")]
    [ProducesResponseType<SubscriptionDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubscriptionDetail>> Reactivate(
        Guid gymId, [FromServices] ReactivateGymHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, cancellationToken));
}
