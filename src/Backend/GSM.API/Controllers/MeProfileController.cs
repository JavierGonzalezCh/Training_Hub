using GSM.API.Auth;
using GSM.API.Uploads;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// "Mi perfil" del miembro (M5): su ficha, sus mediciones y su foto. El cambio de contraseña
/// (<c>POST /api/me/password</c>) y el de correo (<c>request-email-change</c> / <c>confirm-email-change</c>)
/// son de Identity y viven en <c>AuthController</c>.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize(Policy = AuthPolicies.RequireMember)]
public sealed class MeProfileController : ControllerBase
{
    /// <summary>
    /// Su ficha completa, incluida su propia salud (bloqueada sin la autorización de datos). Nunca trae las
    /// notas internas, el origen, las etiquetas ni el referente.
    /// </summary>
    [HttpGet("profile")]
    [EndpointName("GetMyProfile")]
    [ProducesResponseType<MyProfile>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MyProfile>> GetProfile(
        [FromServices] GetMyProfileHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// Edita contacto, emergencia y salud. Un campo de identidad con otro valor → 403; un correo distinto →
    /// 422 (se cambia con verificación); salud siendo menor → 403; salud sin autorización de datos → 409.
    /// </summary>
    [HttpPut("profile")]
    [EndpointName("UpdateMyProfile")]
    [ProducesResponseType<MyProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MyProfile>> UpdateProfile(
        UpdateMyProfileRequest request,
        [FromServices] UpdateMyProfileHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Sube su foto de perfil (reemplaza la anterior).</summary>
    [HttpPost("photo")]
    [EndpointName("UploadMyPhoto")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<MyProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MyProfile>> UploadPhoto(
        IFormFile file, [FromServices] UploadMyPhotoHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(file.ToFileToStore(), cancellationToken));

    /// <summary>Sus mediciones de peso y altura con IMC y evolución. Bloqueadas sin la autorización de datos.</summary>
    [HttpGet("measurements")]
    [EndpointName("ListMyMeasurements")]
    [ProducesResponseType<MemberMeasurementsView>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberMeasurementsView>> ListMeasurements(
        [FromServices] ListMyMeasurementsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Añade una medición propia. 403 siendo menor de edad; 409 sin la autorización de datos; 422 si no vale.</summary>
    [HttpPost("measurements")]
    [EndpointName("AddMyMeasurement")]
    [ProducesResponseType<MemberMeasurementItem>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MemberMeasurementItem>> AddMeasurement(
        AddMemberMeasurementRequest request,
        [FromServices] AddMyMeasurementHandler handler,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await handler.HandleAsync(request, cancellationToken));
}
