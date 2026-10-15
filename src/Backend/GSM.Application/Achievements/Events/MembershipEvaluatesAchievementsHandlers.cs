using GSM.Application.Achievements.Public;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Events;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Achievements.Events;

/// <summary>
/// Una membresía nueva (o una renovación) reevalúa los logros de antigüedad del miembro y los de referidos de
/// quien lo refirió (L2, spec §3.1 y §3.3). No hay scheduler: es la evaluación "perezosa" del momento en que
/// cambia el dato. Idempotente: <c>EvaluateForMemberAsync</c> solo mira los logros que aún no se tienen, así
/// que un reintento del evento no duplica nada.
/// </summary>
internal static class MembershipAchievementEvaluation
{
    public static async Task RunAsync(
        Guid memberId,
        IAchievementService achievements,
        IMemberReader members,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var count = (await achievements.EvaluateForMemberAsync(memberId, cancellationToken)).Count;

        // Si el miembro llegó referido, su primera membresía puede completar un logro de quien lo refirió.
        if (await members.GetReferrerIdAsync(memberId, cancellationToken) is { } referrerId && referrerId != memberId)
        {
            count += (await achievements.EvaluateForMemberAsync(referrerId, cancellationToken)).Count;
        }

        if (count > 0)
        {
            logger.LogInformation(
                "La membresía del miembro {MemberId} desbloqueó {Count} logro(s)", memberId, count);
        }
    }
}

public sealed class MembershipCreatedEvaluatesAchievementsHandler(
    IAchievementService achievements,
    IMemberReader members,
    ILogger<MembershipCreatedEvaluatesAchievementsHandler> logger) : IDomainEventHandler<MembershipCreated>
{
    public Task HandleAsync(MembershipCreated domainEvent, CancellationToken cancellationToken = default)
        => MembershipAchievementEvaluation.RunAsync(domainEvent.MemberId, achievements, members, logger, cancellationToken);
}

public sealed class MembershipRenewedEvaluatesAchievementsHandler(
    IAchievementService achievements,
    IMemberReader members,
    ILogger<MembershipRenewedEvaluatesAchievementsHandler> logger) : IDomainEventHandler<MembershipRenewed>
{
    public Task HandleAsync(MembershipRenewed domainEvent, CancellationToken cancellationToken = default)
        => MembershipAchievementEvaluation.RunAsync(domainEvent.MemberId, achievements, members, logger, cancellationToken);
}
