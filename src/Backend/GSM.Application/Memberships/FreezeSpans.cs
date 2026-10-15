using GSM.Application.Memberships.Abstractions;
using GSM.Domain.Memberships;

namespace GSM.Application.Memberships;

/// <summary>Utilidades de congelamientos compartidas por los casos de uso (MS4).</summary>
internal static class FreezeSpans
{
    public static MembershipSpan SpanOf(Membership m, IEnumerable<MembershipFreeze> allFreezes)
        => new(
            m.Status, m.StartDate, m.EndDate,
            allFreezes.Where(f => f.MembershipId == m.Id).Select(f => new FreezeSpan(f.StartsOn, f.EndsOn)).ToList());

    /// <summary>
    /// Corre las membresías activas encadenadas detrás de <paramref name="originalEnd"/> (el fin de la
    /// membresía ANTES del cambio) y sus congelamientos programados, los mismos días (negativos si se acorta).
    /// </summary>
    public static async Task ShiftFollowingAsync(
        IMembershipRepository memberships, Guid memberId, DateOnly originalEnd, int delta,
        CancellationToken cancellationToken)
    {
        if (delta == 0)
        {
            return;
        }

        var following = await memberships.GetActiveStartingAfterAsync(memberId, originalEnd, cancellationToken);
        if (following.Count == 0)
        {
            return;
        }

        var freezes = await memberships.GetFreezesAsync(following.Select(m => m.Id).ToList(), cancellationToken);
        foreach (var next in following)
        {
            next.ShiftBy(delta);
        }

        foreach (var freeze in freezes)
        {
            freeze.Shift(delta);
        }
    }
}
