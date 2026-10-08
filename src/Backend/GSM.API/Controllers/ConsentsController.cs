using GSM.API.Auth;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Textos de autorización del gym (solo Owner).</summary>
[ApiController]
[Route("api/gym/consent-documents")]
[Authorize(Policy = AuthPolicies.RequireOwner)]
public sealed class ConsentDocumentsController : ControllerBase
{
    /// <summary>El texto vigente de cada autorización: el del gym o, si no publicó, el texto base del sistema.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ConsentDocumentItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ConsentDocumentItem>>> List(
        [FromServices] ListConsentDocumentsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Publica una versión nueva. No anula las aceptaciones de las anteriores.</summary>
    [HttpPut]
    [ProducesResponseType<ConsentDocumentItem>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ConsentDocumentItem>> Publish(
        PublishConsentDocumentRequest request,
        [FromServices] PublishConsentDocumentHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));
}

/// <summary>Autorizaciones de un miembro, desde el panel (staff).</summary>
[ApiController]
[Route("api/members/{memberId:guid}/consents")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class MemberConsentsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MemberConsentStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<MemberConsentStatus>>> List(
        Guid memberId, [FromServices] ListMemberConsentsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, cancellationToken));

    /// <summary>Registra una autorización firmada en papel (canal <c>front_desk</c>).</summary>
    [HttpPost]
    [ProducesResponseType<IReadOnlyList<MemberConsentStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<MemberConsentStatus>>> Register(
        Guid memberId,
        AcceptConsentRequest request,
        [FromServices] RegisterMemberConsentHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, request, cancellationToken));

    /// <summary>Revoca la autorización de datos a pedido del titular.</summary>
    [HttpPost("{type}/revoke")]
    [ProducesResponseType<IReadOnlyList<MemberConsentStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<MemberConsentStatus>>> Revoke(
        Guid memberId,
        string type,
        [FromServices] RevokeMemberConsentHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, type, cancellationToken));
}

/// <summary>Autorizaciones del propio miembro, desde la app.</summary>
[ApiController]
[Route("api/me/consents")]
[Authorize(Policy = AuthPolicies.RequireMember)]
public sealed class MeConsentsController : ControllerBase
{
    /// <summary>Sus autorizaciones con el texto vigente de cada una.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MemberConsentStatus>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MemberConsentStatus>>> List(
        [FromServices] GetMyConsentsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Acepta la versión vigente. Un menor de edad no puede (403): la acepta su acudiente en recepción.</summary>
    [HttpPost]
    [ProducesResponseType<IReadOnlyList<MemberConsentStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<MemberConsentStatus>>> Accept(
        AcceptConsentRequest request, [FromServices] AcceptMyConsentHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Revoca su autorización de datos.</summary>
    [HttpPost("{type}/revoke")]
    [ProducesResponseType<IReadOnlyList<MemberConsentStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<MemberConsentStatus>>> Revoke(
        string type, [FromServices] RevokeMyConsentHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(type, cancellationToken));
}
