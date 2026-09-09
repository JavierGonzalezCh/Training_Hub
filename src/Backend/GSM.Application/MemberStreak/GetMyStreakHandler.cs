using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.MemberStreak;

/// <summary>La racha del socio autenticado (para <c>GET /api/me/streak</c>): racha + congeladores.</summary>
public sealed class GetMyStreakHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    GetMemberStreakHandler inner)
{
    public async Task<MemberStreakResponse> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        return await inner.HandleAsync(self.Id, cancellationToken);
    }
}
