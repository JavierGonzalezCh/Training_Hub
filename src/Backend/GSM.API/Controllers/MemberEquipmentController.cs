using GSM.API.Auth;
using GSM.API.Uploads;
using GSM.Application.Equipment;
using GSM.Application.Equipment.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Máquinas y reportes de falla desde la app del miembro (Equipment §6 y §8). Reportar da 403 si el gym apagó
/// <c>members_can_report_equipment</c>.
/// </summary>
[ApiController]
[Route("api/me/equipment")]
[Authorize(Policy = AuthPolicies.RequireMember)]
public sealed class MemberEquipmentController : ControllerBase
{
    /// <summary>Foto, zona, instrucciones y estado de una máquina, y el reporte sin cerrar del miembro, si lo tiene.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<MyEquipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EndpointName("GetMyEquipment")]
    public async Task<ActionResult<MyEquipmentResponse>> Get(
        Guid id, [FromServices] GetMyEquipmentHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Reportar una falla (motivo, línea opcional). 409 si ya tiene un reporte sin cerrar de esa máquina.</summary>
    [HttpPost("{id:guid}/issues")]
    [ProducesResponseType<MyEquipmentIssueResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [EndpointName("ReportMyEquipmentIssue")]
    public async Task<ActionResult<MyEquipmentIssueResponse>> Report(
        Guid id,
        ReportEquipmentIssueRequest request,
        [FromServices] ReportEquipmentIssueByMemberHandler handler,
        CancellationToken cancellationToken)
    {
        var issue = await handler.HandleAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(MyIssues), issue);
    }

    /// <summary>Foto del reporte propio (JPEG, PNG o WEBP, hasta 5 MB). Se sube después de crearlo.</summary>
    [HttpPost("issues/{issueId:guid}/photo")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<MyEquipmentIssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [EndpointName("UploadMyEquipmentIssuePhoto")]
    public async Task<ActionResult<MyEquipmentIssueResponse>> UploadPhoto(
        Guid issueId,
        IFormFile file,
        [FromServices] UploadEquipmentIssuePhotoHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(issueId, file.ToFileToStore(), cancellationToken));

    /// <summary>Los reportes del miembro y en qué van.</summary>
    [HttpGet("issues")]
    [ProducesResponseType<IReadOnlyList<MyEquipmentIssueResponse>>(StatusCodes.Status200OK)]
    [EndpointName("ListMyEquipmentIssues")]
    public async Task<ActionResult<IReadOnlyList<MyEquipmentIssueResponse>>> MyIssues(
        [FromServices] ListMyEquipmentIssuesHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));
}
