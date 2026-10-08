using GSM.API.Auth;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/membership-plans")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class MembershipPlansController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlanResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlanResponse>>> List(
        [FromQuery] bool includeInactive,
        [FromServices] ListPlansHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(includeInactive, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PlanResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlanResponse>> Get(
        Guid id, [FromServices] GetPlanHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<PlanResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlanResponse>> Create(
        CreatePlanRequest request, [FromServices] CreatePlanHandler handler, CancellationToken cancellationToken)
    {
        var plan = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = plan.Id }, plan);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<PlanResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlanResponse>> Update(
        Guid id, UpdatePlanRequest request, [FromServices] UpdatePlanHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>Vuelve a ofrecer un plan desactivado. Uno que ya está activo → 409.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [EndpointName("ReactivateMembershipPlan")] // evita que NSwag renumere los Reactivate de otros controllers
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<PlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlanResponse>> Reactivate(
        Guid id, [FromServices] ReactivatePlanHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid id, [FromServices] DeletePlanHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }
}
