using GSM.API.Auth;
using GSM.API.Uploads;
using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/achievements")]
[Authorize]
public sealed class AchievementsController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<IReadOnlyList<AchievementResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AchievementResponse>>> List(
        [FromQuery] bool includeInactive,
        [FromServices] ListAchievementsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(includeInactive, cancellationToken));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<AchievementResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AchievementResponse>> Create(
        CreateAchievementRequest request,
        [FromServices] CreateAchievementHandler handler,
        CancellationToken cancellationToken)
    {
        var achievement = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(List), new { }, achievement);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<AchievementResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AchievementResponse>> Update(
        Guid id,
        UpdateAchievementRequest request,
        [FromServices] UpdateAchievementHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid id, [FromServices] DeleteAchievementHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("retroactive-preview")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<RetroactivePreviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RetroactivePreviewResponse>> RetroactivePreview(
        RetroactivePreviewRequest request,
        [FromServices] PreviewRetroactiveAchievementHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    [HttpPost("{id:guid}/grant-retroactive")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<RetroactiveGrantResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RetroactiveGrantResponse>> GrantRetroactive(
        Guid id,
        [FromServices] GrantRetroactiveAchievementHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Otorga a mano un logro de tipo manual a un miembro, con motivo (L4). 409 si ya lo tiene.</summary>
    [HttpPost("{id:guid}/grant-manual")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<MemberAchievementItem>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberAchievementItem>> GrantManual(
        Guid id,
        GrantManualAchievementRequest request,
        [FromServices] GrantManualAchievementHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>Retira un logro manual otorgado por error, con motivo (L4).</summary>
    [HttpPost("{id:guid}/revoke-manual/{memberId:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeManual(
        Guid id,
        Guid memberId,
        RevokeManualAchievementRequest request,
        [FromServices] RevokeManualAchievementHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, memberId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Entrega el premio de un logro ganado por el miembro (L5). 409 si ya se entregó, venció o no hay premio.</summary>
    [HttpPost("{id:guid}/deliver-reward/{memberId:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [EndpointName("DeliverReward")]
    [ProducesResponseType<RewardDeliveredResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RewardDeliveredResponse>> DeliverReward(
        Guid id,
        Guid memberId,
        [FromServices] DeliverRewardHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, memberId, cancellationToken));

    /// <summary>Premios del gym con totales por estado (L5). Solo el Owner. <c>status</c>: pending, delivered o expired.</summary>
    [HttpGet("rewards")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [EndpointName("ListRewards")]
    [ProducesResponseType<RewardsOverviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RewardsOverviewResponse>> ListRewards(
        [FromQuery] string? status,
        [FromServices] ListRewardsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(status, cancellationToken));

    [HttpGet("member/{memberId:guid}")]
    [ProducesResponseType<IReadOnlyList<MemberAchievementItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MemberAchievementItem>>> ForMember(
        Guid memberId,
        [FromServices] GetMemberAchievementsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, cancellationToken));

    [HttpPost("{id:guid}/icon")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<AchievementResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AchievementResponse>> UploadIcon(
        Guid id,
        IFormFile file,
        [FromServices] UploadAchievementIconHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, file.ToFileToStore(), cancellationToken));
}
