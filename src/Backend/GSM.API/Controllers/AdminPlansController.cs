using GSM.API.Auth;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Planes del SaaS con su límite de miembros (SuperAdmin v2, §V1).</summary>
[ApiController]
[Route("api/admin/plans")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
public sealed class AdminPlansController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SaasPlanItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SaasPlanItem>>> List(
        [FromServices] ListSaasPlansHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType<SaasPlanItem>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SaasPlanItem>> Create(
        CreateSaasPlanRequest request, [FromServices] CreateSaasPlanHandler handler, CancellationToken cancellationToken)
    {
        var plan = await handler.HandleAsync(request, cancellationToken);
        return Created($"/api/admin/plans/{plan.Id}", plan);
    }

    [HttpPut("{planId:guid}")]
    [ProducesResponseType<SaasPlanItem>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SaasPlanItem>> Update(
        Guid planId,
        UpdateSaasPlanRequest request,
        [FromServices] UpdateSaasPlanHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(planId, request, cancellationToken));
}
