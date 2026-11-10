using GSM.API.Auth;
using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Plantillas de rutina del gym (R11): rutinas reutilizables que se copian al asignarlas.</summary>
[ApiController]
[Route("api/routine-templates")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class RoutineTemplatesController : ControllerBase
{
    [HttpGet]
    [EndpointName("ListRoutineTemplates")]
    [ProducesResponseType<IReadOnlyList<RoutineTemplateListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoutineTemplateListItem>>> List(
        [FromServices] ListRoutineTemplatesHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>RT2: galería de portadas que una plantilla puede usar.</summary>
    [HttpGet("images")]
    [EndpointName("ListRoutineImages")]
    [ProducesResponseType<IReadOnlyList<RoutineImageResponse>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<RoutineImageResponse>> Images([FromServices] ListRoutineImagesHandler handler)
        => Ok(handler.HandleAsync());

    [HttpGet("{id:guid}")]
    [EndpointName("GetRoutineTemplate")]
    [ProducesResponseType<RoutineTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoutineTemplateResponse>> Get(
        Guid id, [FromServices] GetRoutineTemplateHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    [HttpPost]
    [EndpointName("CreateRoutineTemplate")]
    [ProducesResponseType<RoutineTemplateListItem>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoutineTemplateListItem>> Create(
        SaveRoutineTemplateRequest request,
        [FromServices] CreateRoutineTemplateHandler handler,
        CancellationToken cancellationToken)
    {
        var created = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [EndpointName("UpdateRoutineTemplate")]
    [ProducesResponseType<RoutineTemplateListItem>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoutineTemplateListItem>> Update(
        Guid id,
        SaveRoutineTemplateRequest request,
        [FromServices] UpdateRoutineTemplateHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>Borrar una plantilla (R11-1): solo el Owner y el staff con el permiso <c>Trainer</c>; al resto, 403.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(StaffPermission.Trainer)]
    [EndpointName("DeleteRoutineTemplate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id, [FromServices] DeleteRoutineTemplateHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Guarda una rutina existente (de cualquier miembro del gym) como plantilla nueva.</summary>
    [HttpPost("from-routine/{routineId:guid}")]
    [EndpointName("SaveRoutineAsTemplate")]
    [ProducesResponseType<RoutineTemplateListItem>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoutineTemplateListItem>> FromRoutine(
        Guid routineId,
        SaveRoutineAsTemplateRequest request,
        [FromServices] SaveRoutineAsTemplateHandler handler,
        CancellationToken cancellationToken)
    {
        var created = await handler.HandleAsync(routineId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>
    /// Asigna la plantilla a un miembro: copia su contenido como rutina nueva y desactiva la que tuviera activa.
    /// 422 si algún ejercicio ya no está disponible para el gym.
    /// </summary>
    [HttpPost("{id:guid}/assign/{memberId:guid}")]
    [EndpointName("AssignRoutineTemplate")]
    [ProducesResponseType<RoutineResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoutineResponse>> Assign(
        Guid id, Guid memberId,
        [FromServices] AssignRoutineFromTemplateHandler handler,
        CancellationToken cancellationToken)
    {
        var routine = await handler.HandleAsync(memberId, id, cancellationToken);
        return CreatedAtAction(nameof(RoutinesController.Get), "Routines", new { id = routine.Id }, routine);
    }
}
