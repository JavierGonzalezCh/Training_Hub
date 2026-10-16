using GSM.API.Auth;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Planes de grupo (MS7, Memberships v2 §9): familiar, de pareja o corporativo. Cada persona tiene su membresía con el
/// mismo grupo, fechas y ciclo. Congelar, reanudar y cancelar el grupo usan los endpoints de membresía de siempre sobre
/// la membresía del titular (cancelar la de un beneficiario lo saca del grupo).
/// </summary>
[ApiController]
[Route("api/membership-groups")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class MembershipGroupsController : ControllerBase
{
    /// <summary>
    /// Asigna un plan de grupo: la membresía del titular (que paga) y las de los beneficiarios, con las mismas fechas. La UI
    /// cobra al titular el `totalToCharge` de la respuesta con una segunda llamada a Payments.
    /// </summary>
    [HttpPost]
    [EndpointName("AssignGroupMembership")]
    [ProducesResponseType<MembershipGroupResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MembershipGroupResponse>> Assign(
        AssignGroupMembershipRequest request,
        [FromServices] AssignGroupMembershipHandler handler,
        CancellationToken cancellationToken)
    {
        var group = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { groupId = group.Id }, group);
    }

    /// <summary>El grupo con sus integrantes, cupos y lo que se cobra al titular.</summary>
    [HttpGet("{groupId:guid}")]
    [EndpointName("GetMembershipGroup")]
    [ProducesResponseType<MembershipGroupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MembershipGroupResponse>> Get(
        Guid groupId,
        [FromServices] GetMembershipGroupHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(groupId, cancellationToken));

    /// <summary>
    /// Renueva el grupo: crea otro que sigue al anterior (regla de renovación de §4.1 sobre el historial del titular).
    /// Conserva a los beneficiarios que siguen, salvo que se mande otra lista.
    /// </summary>
    [HttpPost("{groupId:guid}/renew")]
    [EndpointName("RenewGroupMembership")]
    [ProducesResponseType<MembershipGroupResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MembershipGroupResponse>> Renew(
        Guid groupId,
        RenewGroupMembershipRequest request,
        [FromServices] RenewGroupMembershipHandler handler,
        CancellationToken cancellationToken)
    {
        var group = await handler.HandleAsync(groupId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { groupId = group.Id }, group);
    }

    /// <summary>Mete a otro miembro al grupo mientras haya cupo: hereda las fechas actuales y no paga.</summary>
    [HttpPost("{groupId:guid}/beneficiaries")]
    [EndpointName("AddGroupBeneficiary")]
    [ProducesResponseType<MembershipGroupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MembershipGroupResponse>> AddBeneficiary(
        Guid groupId,
        AddGroupBeneficiaryRequest request,
        [FromServices] AddGroupBeneficiaryHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(groupId, request, cancellationToken));
}
