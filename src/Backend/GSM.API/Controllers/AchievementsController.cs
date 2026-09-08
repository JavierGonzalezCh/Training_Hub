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
