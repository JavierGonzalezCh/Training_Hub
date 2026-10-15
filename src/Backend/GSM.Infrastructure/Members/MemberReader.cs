using GSM.Application.Members.Public;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Members;

/// <summary>
/// Lectura para otros módulos. Respeta el Global Query Filter: los datos de miembro son
/// intrínsecamente del tenant y el llamador (Attendance, etc.) siempre opera en su gym.
/// </summary>
public sealed class MemberReader(GsmDbContext dbContext) : IMemberReader
{
    private IQueryable<Member> Members => dbContext.Set<Member>();

    public Task<MemberInfo?> GetAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Project(Members.Where(m => m.Id == memberId)).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Members.AnyAsync(m => m.Id == memberId, cancellationToken);

    public Task<MemberInfo?> ResolveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => Project(Members.IgnoreQueryFilters().Where(m => m.UserId == userId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> GetActiveCountAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Members.CountAsync(m => m.GymId == gymId && m.Status == MemberStatus.Active, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetActiveCountsByGymAsync(
        CancellationToken cancellationToken = default)
        => await Members
            .Where(m => m.Status == MemberStatus.Active)
            .GroupBy(m => m.GymId)
            .Select(g => new { GymId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GymId, x => x.Count, cancellationToken);

    public Task<MemberContactView?> GetContactAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Members.Where(m => m.Id == memberId)
            .Select(m => new MemberContactView(m.Id, m.GymId, m.FirstName, m.Email, m.Status))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MemberContactView>> GetEmailContactsAsync(
        Guid gymId, CancellationToken cancellationToken = default)
        => await Members
            .Where(m => m.GymId == gymId && m.Status == MemberStatus.Active && m.Email != null && m.Email != "")
            .Select(m => new MemberContactView(m.Id, m.GymId, m.FirstName, m.Email, m.Status))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetActiveMemberIdsAsync(
        Guid gymId, CancellationToken cancellationToken = default)
        => await Members
            .Where(m => m.GymId == gymId && m.Status == MemberStatus.Active)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetWhatsAppPhonesAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await Members
            .Where(m => memberIds.Contains(m.Id) && m.Phone != null)
            .Select(m => new { m.Id, m.Phone })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, string>();
        foreach (var row in rows)
        {
            var normalized = PhoneNumber.TryNormalize(row.Phone);
            if (normalized is not null)
            {
                result[row.Id] = normalized;
            }
        }

        return result;
    }

    public Task<MemberInvitationView?> GetPendingInvitationAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => dbContext.Set<MemberInvitation>()
            .Where(i => i.MemberId == memberId && i.RedeemedAt == null)
            .OrderByDescending(i => i.ExpiresAt)
            .Select(i => new MemberInvitationView(i.Code, i.ExpiresAt))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HasDataProcessingConsentAsync(Guid memberId, CancellationToken cancellationToken = default)
        => HasConsentAsync(memberId, ConsentType.DataProcessing, cancellationToken);

    public Task<bool> HasLiabilityWaiverAsync(Guid memberId, CancellationToken cancellationToken = default)
        => HasConsentAsync(memberId, ConsentType.LiabilityWaiver, cancellationToken);

    // La aceptación más reciente del tipo, y que no esté revocada.
    private async Task<bool> HasConsentAsync(Guid memberId, ConsentType type, CancellationToken cancellationToken)
    {
        var latest = await dbContext.Set<MemberConsent>()
            .Where(c => c.MemberId == memberId && c.Type == type)
            .OrderByDescending(c => c.AcceptedAt).ThenByDescending(c => c.Id)
            .Select(c => new { c.RevokedAt })
            .FirstOrDefaultAsync(cancellationToken);
        return latest is not null && latest.RevokedAt is null;
    }

    private static IQueryable<MemberInfo> Project(IQueryable<Member> source)
        => source.Select(m => new MemberInfo(
            m.Id, m.GymId, m.UserId, m.FirstName, m.LastName, m.Status, m.PhotoUrl,
            m.MedicalConditions != null || m.TrainingRestrictions != null));
}
