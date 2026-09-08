using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/members/{memberId:guid}/payments")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class MemberPaymentsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<PaymentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PaymentResponse>>> List(
        Guid memberId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] ListPaymentsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(
            new ListPaymentsQuery
            {
                MemberId = memberId,
                Page = page <= 0 ? 1 : page,
                PageSize = pageSize <= 0 ? 20 : pageSize,
            },
            cancellationToken));
}
