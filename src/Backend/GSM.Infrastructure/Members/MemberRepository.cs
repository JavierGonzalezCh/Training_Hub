using GSM.Application.Members.Abstractions;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Members;

public sealed class MemberRepository(GsmDbContext dbContext) : IMemberRepository
{
    private DbSet<Member> Members => dbContext.Set<Member>();

    public Task<Member?> GetByIdAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Members.FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);

    public async Task<(IReadOnlyList<Member> Items, int Total)> GetPageAsync(
        MemberPageQuery query, CancellationToken cancellationToken = default)
    {
        var q = Members.AsQueryable();

        if (query.Status is { } status)
        {
            q = q.Where(m => m.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            q = q.Where(m =>
                EF.Functions.ILike(m.FirstName, term) ||
                EF.Functions.ILike(m.LastName, term) ||
                EF.Functions.ILike(m.DocumentNumber, term));
        }

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderBy(m => m.LastName).ThenBy(m => m.FirstName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<bool> DocumentExistsAsync(
        string documentNumber, Guid? excludeMemberId, CancellationToken cancellationToken = default)
        => Members.AnyAsync(
            m => m.DocumentNumber == documentNumber && (excludeMemberId == null || m.Id != excludeMemberId),
            cancellationToken);

    public Task<bool> QrExistsAsync(string qrCode, CancellationToken cancellationToken = default)
        => Members.AnyAsync(m => m.QrCode == qrCode, cancellationToken);

    public void Add(Member member) => Members.Add(member);
}
