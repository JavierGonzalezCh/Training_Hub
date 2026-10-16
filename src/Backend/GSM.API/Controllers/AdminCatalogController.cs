using GSM.API.Auth;
using GSM.API.Uploads;
using GSM.Application.Equipment;
using GSM.Application.Equipment.Contracts;
using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Catálogos del sistema para el SuperAdmin (S4, SuperAdmin v2 §V7): ejercicios, alternativas recomendadas, modelos
/// de máquina y ligas ejercicio ↔ modelo. Un SuperAdmin no tiene gym, así que estas rutas trabajan solo con filas del
/// sistema (<c>gym_id IS NULL</c>) sin pasar por el contexto de gym. No hay lógica nueva aquí: los casos de uso viven en
/// Routines y Equipment, que son los dueños de esos datos, y verifican por su cuenta que quien llama sea SuperAdmin.
/// </summary>
[ApiController]
[Route("api/admin/catalog")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
public sealed class AdminCatalogController : ControllerBase
{
    // --- Ejercicios del sistema ---

    /// <summary>Ejercicios del sistema. Con <c>includeInactive</c> salen también los desactivados.</summary>
    [HttpGet("exercises")]
    [ProducesResponseType<IReadOnlyList<ExerciseResponse>>(StatusCodes.Status200OK)]
    [EndpointName("AdminCatalogListExercises")]
    public async Task<ActionResult<IReadOnlyList<ExerciseResponse>>> ListExercises(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive,
        [FromServices] ListExercisesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(
            new ExerciseFilter(search, null, null, includeInactive), cancellationToken));

    [HttpPost("exercises")]
    [ProducesResponseType<ExerciseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [EndpointName("AdminCatalogCreateExercise")]
    public async Task<ActionResult<ExerciseResponse>> CreateExercise(
        CreateExerciseRequest request,
        [FromServices] CreateExerciseHandler handler,
        CancellationToken cancellationToken)
    {
        var exercise = await handler.HandleAsync(request, cancellationToken);
        return Created($"/api/admin/catalog/exercises/{exercise.Id}", exercise);
    }

    [HttpPut("exercises/{id:guid}")]
    [ProducesResponseType<ExerciseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [EndpointName("AdminCatalogUpdateExercise")]
    public async Task<ActionResult<ExerciseResponse>> UpdateExercise(
        Guid id,
        UpdateExerciseRequest request,
        [FromServices] UpdateExerciseHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    [HttpPost("exercises/{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [EndpointName("AdminCatalogDeactivateExercise")]
    public async Task<IActionResult> DeactivateExercise(
        Guid id, [FromServices] DeactivateExerciseHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("exercises/{id:guid}/activate")]
    [ProducesResponseType<ExerciseResponse>(StatusCodes.Status200OK)]
    [EndpointName("AdminCatalogActivateExercise")]
    public async Task<ActionResult<ExerciseResponse>> ActivateExercise(
        Guid id, [FromServices] ActivateExerciseHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Imagen del ejercicio (JPEG, PNG o WEBP, hasta 5 MB).</summary>
    [HttpPost("exercises/{id:guid}/image")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ExerciseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EndpointName("AdminCatalogUploadExerciseImage")]
    public async Task<ActionResult<ExerciseResponse>> UploadExerciseImage(
        Guid id, IFormFile file, [FromServices] UploadExerciseImageHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, file.ToFileToStore(), cancellationToken));

    // --- Alternativas recomendadas del sistema ---

    /// <summary>Alternativas del ejercicio: las recomendadas del sistema van primero (<c>Source = recommended</c>).</summary>
    [HttpGet("exercises/{id:guid}/alternatives")]
    [ProducesResponseType<IReadOnlyList<ExerciseAlternativeResponse>>(StatusCodes.Status200OK)]
    [EndpointName("AdminCatalogGetAlternatives")]
    public async Task<ActionResult<IReadOnlyList<ExerciseAlternativeResponse>>> GetAlternatives(
        Guid id, [FromServices] GetExerciseAlternativesHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    [HttpPut("exercises/{id:guid}/alternatives/{alternativeId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [EndpointName("AdminCatalogAddAlternative")]
    public async Task<IActionResult> AddAlternative(
        Guid id, Guid alternativeId, [FromServices] AddExerciseAlternativeHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, alternativeId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("exercises/{id:guid}/alternatives/{alternativeId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [EndpointName("AdminCatalogRemoveAlternative")]
    public async Task<IActionResult> RemoveAlternative(
        Guid id, Guid alternativeId, [FromServices] RemoveExerciseAlternativeHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, alternativeId, cancellationToken);
        return NoContent();
    }

    // --- Ligas ejercicio <-> modelo ---

    /// <summary>Modelos del catálogo a los que está ligado el ejercicio del sistema.</summary>
    [HttpGet("exercises/{id:guid}/equipment-models")]
    [ProducesResponseType<ExerciseEquipmentLinksResponse>(StatusCodes.Status200OK)]
    [EndpointName("AdminCatalogGetExerciseModels")]
    public async Task<ActionResult<ExerciseEquipmentLinksResponse>> GetExerciseModels(
        Guid id, [FromServices] GetExerciseEquipmentHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Deja el ejercicio ligado exactamente a estos modelos (máximo 20; vacío = ninguno).</summary>
    [HttpPut("exercises/{id:guid}/equipment-models")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [EndpointName("AdminCatalogSetExerciseModels")]
    public async Task<IActionResult> SetExerciseModels(
        Guid id,
        SetExerciseEquipmentModelsRequest request,
        [FromServices] SetExerciseEquipmentModelsHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, request, cancellationToken);
        return NoContent();
    }

    // --- Modelos de máquina ---

    /// <summary>Catálogo de máquinas del sistema. Con <c>includeInactive</c> salen también los desactivados.</summary>
    [HttpGet("equipment-models")]
    [ProducesResponseType<IReadOnlyList<AdminEquipmentModelResponse>>(StatusCodes.Status200OK)]
    [EndpointName("AdminCatalogListModels")]
    public async Task<ActionResult<IReadOnlyList<AdminEquipmentModelResponse>>> ListModels(
        [FromQuery] bool includeInactive,
        [FromServices] ListAdminEquipmentModelsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(includeInactive, cancellationToken));

    [HttpPost("equipment-models")]
    [ProducesResponseType<AdminEquipmentModelResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [EndpointName("AdminCatalogCreateModel")]
    public async Task<ActionResult<AdminEquipmentModelResponse>> CreateModel(
        SaveEquipmentModelRequest request,
        [FromServices] CreateEquipmentModelHandler handler,
        CancellationToken cancellationToken)
    {
        var model = await handler.HandleAsync(request, cancellationToken);
        return Created($"/api/admin/catalog/equipment-models/{model.Id}", model);
    }

    [HttpPut("equipment-models/{id:guid}")]
    [ProducesResponseType<AdminEquipmentModelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [EndpointName("AdminCatalogUpdateModel")]
    public async Task<ActionResult<AdminEquipmentModelResponse>> UpdateModel(
        Guid id,
        SaveEquipmentModelRequest request,
        [FromServices] UpdateEquipmentModelHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    [HttpPost("equipment-models/{id:guid}/deactivate")]
    [ProducesResponseType<AdminEquipmentModelResponse>(StatusCodes.Status200OK)]
    [EndpointName("AdminCatalogDeactivateModel")]
    public async Task<ActionResult<AdminEquipmentModelResponse>> DeactivateModel(
        Guid id, [FromServices] SetEquipmentModelActiveHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, false, cancellationToken));

    [HttpPost("equipment-models/{id:guid}/activate")]
    [ProducesResponseType<AdminEquipmentModelResponse>(StatusCodes.Status200OK)]
    [EndpointName("AdminCatalogActivateModel")]
    public async Task<ActionResult<AdminEquipmentModelResponse>> ActivateModel(
        Guid id, [FromServices] SetEquipmentModelActiveHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, true, cancellationToken));

    /// <summary>Imagen de referencia del modelo (JPEG, PNG o WEBP, hasta 5 MB).</summary>
    [HttpPost("equipment-models/{id:guid}/image")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<AdminEquipmentModelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EndpointName("AdminCatalogUploadModelImage")]
    public async Task<ActionResult<AdminEquipmentModelResponse>> UploadModelImage(
        Guid id, IFormFile file, [FromServices] UploadEquipmentModelImageHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, file.ToFileToStore(), cancellationToken));
}
