using GSM.API.Auth;
using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Definiciones de clase, gestionadas por el staff. El horario proyectado, las sesiones y las
/// reservas llegan en sub-etapas posteriores. Con <c>show_classes = false</c> el staff sigue operando.
/// </summary>
[ApiController]
[Route("api/classes")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class ClassesController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ClassListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClassListItem>>> List(
        [FromQuery] bool includeInactive,
        [FromServices] ListClassesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(includeInactive, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClassResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClassResponse>> Get(
        Guid id, [FromServices] GetClassHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Horario proyectado del rango: ocurrencias calculadas de la recurrencia + sesiones ya materializadas.</summary>
    [HttpGet("schedule")]
    [ProducesResponseType<ScheduleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ScheduleResponse>> Schedule(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] GetScheduleHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(from, to, cancellationToken));

    /// <summary>Cancela una ocurrencia puntual; materializa la sesión con <c>status = cancelled</c>.</summary>
    [HttpPost("{id:guid}/sessions/{date}/cancel")]
    [ProducesResponseType<ScheduleEntry>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ScheduleEntry>> CancelSession(
        Guid id,
        DateOnly date,
        CancelSessionRequest? request,
        [FromServices] CancelSessionHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, date, request?.Reason, cancellationToken));

    [HttpPost]
    [ProducesResponseType<ClassResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ClassResponse>> Create(
        SaveClassRequest request,
        [FromServices] CreateClassHandler handler,
        CancellationToken cancellationToken)
    {
        var gymClass = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = gymClass.Id }, gymClass);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ClassResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ClassResponse>> Update(
        Guid id,
        SaveClassRequest request,
        [FromServices] UpdateClassHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        Guid id, [FromServices] DeactivateClassHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }
}
