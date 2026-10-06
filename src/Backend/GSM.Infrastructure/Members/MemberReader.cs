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

    private static IQueryable<MemberInfo> Project(IQueryable<Member> source)
        => source.Select(m => new MemberInfo(
            m.Id, m.GymId, m.UserId, m.FirstName, m.LastName, m.Status));
}
