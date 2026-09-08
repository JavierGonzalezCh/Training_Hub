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

    public Task<MemberInfo?> ResolveByQrAsync(string qrCode, CancellationToken cancellationToken = default)
        => Project(Members.Where(m => m.QrCode == qrCode)).FirstOrDefaultAsync(cancellationToken);

    public Task<MemberInfo?> ResolveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => Project(Members.IgnoreQueryFilters().Where(m => m.UserId == userId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> GetActiveCountAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Members.CountAsync(m => m.GymId == gymId && m.Status == MemberStatus.Active, cancellationToken);

    private static IQueryable<MemberInfo> Project(IQueryable<Member> source)
        => source.Select(m => new MemberInfo(
            m.Id, m.GymId, m.UserId, m.FirstName, m.LastName, m.Status, m.QrCode));
}
