using GSM.API.Auth;
using GSM.API.Uploads;
using GSM.Application.Equipment;
using GSM.Application.Equipment.Abstractions;
using GSM.Application.Equipment.Contracts;
using GSM.Domain.Equipment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Inventario del gym (máquinas y equipos) y catálogo de modelos del sistema para armarlo. Owner y staff
/// gestionan; solo el Owner da de baja.
/// </summary>
[ApiController]
[Route("api/equipment")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class EquipmentController : ControllerBase
{
    /// <summary>Catálogo del sistema para elegir, con filtro opcional por tipo.</summary>
    [HttpGet("models")]
    [ProducesResponseType<IReadOnlyList<EquipmentModelResponse>>(StatusCodes.Status200OK)]
    [EndpointName("ListEquipmentModels")]
    public async Task<ActionResult<IReadOnlyList<EquipmentModelResponse>>> ListModels(
        [FromQuery] EquipmentType? type,
        [FromServices] ListEquipmentModelsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(type, cancellationToken));

    /// <summary>Inventario del gym, con filtros por tipo, zona y estado. Las bajas no salen salvo que se pidan.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EquipmentItemResponse>>(StatusCodes.Status200OK)]
    [EndpointName("ListEquipment")]
    public async Task<ActionResult<IReadOnlyList<EquipmentItemResponse>>> List(
        [FromQuery] EquipmentType? type,
        [FromQuery] string? zone,
        [FromQuery] EquipmentStatus? status,
        [FromQuery] string? search,
        [FromQuery] bool includeInactive,
        [FromServices] ListEquipmentHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(
            new EquipmentFilter(type, zone, status, search, includeInactive), cancellationToken));

    /// <summary>Zonas ya usadas en el gym, para autocompletar.</summary>
    [HttpGet("zones")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    [EndpointName("ListEquipmentZones")]
    public async Task<ActionResult<IReadOnlyList<string>>> ListZones(
        [FromServices] ListEquipmentZonesHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<EquipmentItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EndpointName("GetEquipment")]
    public async Task<ActionResult<EquipmentItemResponse>> Get(
        Guid id, [FromServices] GetEquipmentHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Máquina propia del gym, creada a mano.</summary>
    [HttpPost]
    [ProducesResponseType<EquipmentItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [EndpointName("CreateEquipment")]
    public async Task<ActionResult<EquipmentItemResponse>> Create(
        SaveEquipmentRequest request,
        [FromServices] CreateEquipmentHandler handler,
        CancellationToken cancellationToken)
    {
        var item = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    /// <summary>
    /// Agrega varios modelos del catálogo de una vez. Una fila por unidad en las máquinas; una fila con
    /// cantidad en los implementos sueltos.
    /// </summary>
    [HttpPost("from-models")]
    [ProducesResponseType<IReadOnlyList<EquipmentItemResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EndpointName("AddEquipmentFromModels")]
    public async Task<ActionResult<IReadOnlyList<EquipmentItemResponse>>> AddFromModels(
        AddEquipmentFromModelsRequest request,
        [FromServices] AddEquipmentFromModelsHandler handler,
        CancellationToken cancellationToken)
    {
        var created = await handler.HandleAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<EquipmentItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EndpointName("UpdateEquipment")]
    public async Task<ActionResult<EquipmentItemResponse>> Update(
        Guid id,
        SaveEquipmentRequest request,
        [FromServices] UpdateEquipmentHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>Foto de la máquina real (JPEG, PNG o WEBP, hasta 5 MB).</summary>
    [HttpPost("{id:guid}/photo")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<EquipmentItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EndpointName("UploadEquipmentPhoto")]
    public async Task<ActionResult<EquipmentItemResponse>> UploadPhoto(
        Guid id,
        IFormFile file,
        [FromServices] UploadEquipmentPhotoHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, file.ToFileToStore(), cancellationToken));

    /// <summary>Baja lógica. Solo el Owner.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EndpointName("DeactivateEquipment")]
    public async Task<IActionResult> Deactivate(
        Guid id, [FromServices] DeactivateEquipmentHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }
}
