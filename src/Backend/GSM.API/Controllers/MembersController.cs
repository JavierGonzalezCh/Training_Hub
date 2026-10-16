using GSM.API.Auth;
using GSM.API.Uploads;
using GSM.Application.Common;
using GSM.Application.Identity;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Application.Payments.Public;
using GSM.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/members")]
[Authorize]
public sealed class MembersController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<PagedResult<MemberListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MemberListItem>>> List(
        [FromQuery] ListMembersQuery query,
        [FromServices] ListMembersHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(query, cancellationToken));

    /// <summary>Quienes pueden ser entrenador asignado: el Owner y el staff con el permiso Trainer.</summary>
    [HttpGet("trainers")]
    [EndpointName("ListMemberTrainers")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<IReadOnlyList<TrainerOption>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TrainerOption>>> ListTrainers(
        [FromServices] ListTrainersHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Asigna, cambia o quita (<c>trainerUserId</c> nulo) el entrenador del miembro. 422 si no puede serlo.</summary>
    [HttpPut("{id:guid}/trainer")]
    [EndpointName("AssignMemberTrainer")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<MemberDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MemberDetail>> AssignTrainer(
        Guid id,
        AssignTrainerRequest request,
        [FromServices] AssignMemberTrainerHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<MemberDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MemberDetail>> Get(
        Guid id,
        [FromServices] GetMemberHandler handler,
        [FromServices] IMembershipReader memberships,
        [FromServices] IPaymentReader payments,
        CancellationToken cancellationToken)
    {
        var member = await handler.HandleAsync(id, cancellationToken);

        var active = await memberships.GetActiveAsync(id, cancellationToken);
        var recent = await payments.GetRecentByMemberAsync(id, limit: 5, cancellationToken);
        var access = await memberships.GetAccessStatusAsync(id, cancellationToken);

        return Ok(member with
        {
            CommercialStatus = GSM.Domain.Members.EnumText.Snake(access.Commercial)!,
            AccessNotice = access.Notice ?? (access.StartsOn is null ? null : access.BlockMessage),
            AccessStartsOn = access.StartsOn,
            CurrentMembership = active is null
                ? null
                : new MemberCurrentMembership(
                    active.MembershipId, active.PlanId, active.PlanName, active.StartDate, active.EndDate,
                    access.EntriesTotal, access.EntriesLeft),
            RecentPayments = recent
                .Select(p => new MemberRecentPayment(
                    p.Id, p.Amount, p.Method.ToString(), p.Concept, p.PaidAt))
                .ToList(),
        });
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<CreateMemberResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateMemberResponse>> Create(
        CreateMemberRequest request, [FromServices] CreateMemberHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<MemberDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MemberDetail>> Update(
        Guid id,
        UpdateMemberRequest request,
        [FromServices] UpdateMemberHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(
        Guid id, [FromServices] DeactivateMemberHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Devuelve a activo a un miembro dado de baja. Uno que ya está activo → 409.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [EndpointName("ReactivateMember")] // evita que NSwag renumere los Reactivate de otros controllers
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<MemberDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MemberDetail>> Reactivate(
        Guid id, [FromServices] ReactivateMemberHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Historial de cambios de la ficha, del más reciente al más antiguo.</summary>
    [HttpGet("{id:guid}/changes")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<PagedResult<MemberChangeItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<MemberChangeItem>>> ListChanges(
        Guid id,
        [FromQuery] ListMemberChangesQuery query,
        [FromServices] ListMemberChangesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, query, cancellationToken));

    /// <summary>
    /// Sección de salud de la ficha. Exige el permiso <c>ViewMemberHealth</c> (el Owner lo tiene siempre).
    /// Sin la autorización de datos del miembro llega bloqueada (<c>consentRequired</c>). Cada consulta
    /// queda registrada (una por usuario, miembro y día).
    /// </summary>
    [HttpGet("{id:guid}/health")]
    [EndpointName("GetMemberHealth")]
    [RequirePermission(StaffPermission.ViewMemberHealth)]
    [ProducesResponseType<MemberHealthView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MemberHealthView>> GetHealth(
        Guid id, [FromServices] GetMemberHealthHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Reemplaza los datos de salud. 409 si el miembro no tiene la autorización de datos.</summary>
    [HttpPut("{id:guid}/health")]
    [EndpointName("UpdateMemberHealth")]
    [RequirePermission(StaffPermission.ViewMemberHealth)]
    [ProducesResponseType<MemberHealth>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MemberHealth>> UpdateHealth(
        Guid id,
        UpdateMemberHealthRequest request,
        [FromServices] UpdateMemberHealthHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>
    /// Mediciones de peso y altura (con IMC calculado). Son datos de salud: exige <c>ViewMemberHealth</c> y,
    /// sin la autorización de datos del miembro, llegan bloqueadas (<c>consentRequired</c>).
    /// </summary>
    [HttpGet("{id:guid}/measurements")]
    [EndpointName("ListMemberMeasurements")]
    [RequirePermission(StaffPermission.ViewMemberHealth)]
    [ProducesResponseType<MemberMeasurementsView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MemberMeasurementsView>> ListMeasurements(
        Guid id, [FromServices] ListMemberMeasurementsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Añade una medición. 409 sin la autorización de datos; 422 si el peso, la altura o la fecha no valen.</summary>
    [HttpPost("{id:guid}/measurements")]
    [EndpointName("AddMemberMeasurement")]
    [RequirePermission(StaffPermission.ViewMemberHealth)]
    [ProducesResponseType<MemberMeasurementItem>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MemberMeasurementItem>> AddMeasurement(
        Guid id,
        AddMemberMeasurementRequest request,
        [FromServices] AddMemberMeasurementHandler handler,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>Borra una medición mal digitada; el borrado queda en el historial de cambios.</summary>
    [HttpDelete("{id:guid}/measurements/{measurementId:guid}")]
    [EndpointName("DeleteMemberMeasurement")]
    [RequirePermission(StaffPermission.ViewMemberHealth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMeasurement(
        Guid id, Guid measurementId, [FromServices] DeleteMemberMeasurementHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, measurementId, cancellationToken);
        return NoContent();
    }

    /// <summary>Quién vio los datos de salud del miembro (solo el Owner), del más reciente al más antiguo.</summary>
    [HttpGet("{id:guid}/health-access")]
    [EndpointName("ListMemberHealthAccess")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<PagedResult<MemberHealthAccessItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<MemberHealthAccessItem>>> ListHealthAccess(
        Guid id,
        [FromQuery] ListMemberChangesQuery query,
        [FromServices] ListMemberHealthAccessHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, query, cancellationToken));

    /// <summary>Notas internas del miembro (solo el equipo del gym; el miembro nunca las ve).</summary>
    [HttpGet("{id:guid}/notes")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<IReadOnlyList<MemberNoteItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<MemberNoteItem>>> ListNotes(
        Guid id, [FromServices] ListMemberNotesHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    [HttpPost("{id:guid}/notes")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<MemberNoteItem>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MemberNoteItem>> AddNote(
        Guid id,
        AddMemberNoteRequest request,
        [FromServices] AddMemberNoteHandler handler,
        CancellationToken cancellationToken)
    {
        var note = await handler.HandleAsync(id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, note);
    }

    [HttpPost("{id:guid}/photo")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<MemberDetail>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberDetail>> UploadPhoto(
        Guid id,
        IFormFile file,
        [FromServices] UploadMemberPhotoHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, file.ToFileToStore(), cancellationToken));

    /// <summary>Genera un código para que el miembro cree su cuenta de la app.</summary>
    [HttpPost("{id:guid}/invite")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<InviteMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InviteMemberResponse>> Invite(
        Guid id, [FromServices] InviteMemberHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>
    /// Canje anónimo del código: crea la cuenta del miembro (o, si su email ya tiene cuenta, valida su
    /// contraseña y agrega el acceso), lo vincula y deja la sesión iniciada.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<RegisterMemberResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterMemberResponse>> Register(
        RegisterMemberRequest request,
        [FromServices] RegisterMemberHandler handler,
        [FromServices] StartMemberSessionHandler startSession,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        // El canje ya confirmó; la sesión la emite Identity, no Members.
        var session = await startSession.HandleAsync(result.UserId, result.GymId, cancellationToken);
        return CreatedAtAction(
            nameof(Get), new { id = result.MemberId }, new RegisterMemberResponse(result.MemberId, result.UserId, session));
    }
}
