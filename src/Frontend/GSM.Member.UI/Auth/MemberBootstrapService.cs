using GSM.Client.Generated;

namespace GSM.Member.UI.Auth;

/// <summary>Resuelve el <see cref="MemberContext"/> del miembro autenticado.</summary>
public sealed class MemberBootstrapService(IMeClient me, IMembersClient members, MemberContext context)
{
    public async Task<MemberDetail> LoadAsync(CancellationToken cancellationToken = default)
    {
        var streak = await me.StreakAsync(cancellationToken);
        var detail = await members.MembersGET2Async(streak.MemberId, cancellationToken);
        context.Set(detail);
        return detail;
    }
}
