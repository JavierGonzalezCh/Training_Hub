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

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim();
            q = q.Where(m => m.Tags.Contains(tag));
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
        DocumentType documentType, string documentNumber, Guid? excludeMemberId,
        CancellationToken cancellationToken = default)
        => Members.AnyAsync(
            m => m.DocumentType == documentType && m.DocumentNumber == documentNumber
                && (excludeMemberId == null || m.Id != excludeMemberId),
            cancellationToken);

    public Task<bool> ExistsAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Members.AnyAsync(m => m.Id == memberId, cancellationToken);

    public void Add(Member member) => Members.Add(member);

    // --- Historial de cambios ---

    public void Add(MemberChange change) => dbContext.Set<MemberChange>().Add(change);

    public async Task<(IReadOnlyList<MemberChange> Items, int Total)> GetChangesPageAsync(
        Guid memberId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var q = dbContext.Set<MemberChange>().Where(c => c.MemberId == memberId);
        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    // --- Notas internas ---

    public void Add(MemberNote note) => dbContext.Set<MemberNote>().Add(note);

    public async Task<IReadOnlyList<MemberNote>> GetNotesAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await dbContext.Set<MemberNote>()
            .Where(n => n.MemberId == memberId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .ToListAsync(cancellationToken);

    // --- Invitaciones ---

    private DbSet<MemberInvitation> Invitations => dbContext.Set<MemberInvitation>();

    public void Add(MemberInvitation invitation) => Invitations.Add(invitation);

    // El código de invitación es un identificador único GLOBAL (así lo exige el índice de la BD):
    // estas consultas ignoran el query filter de tenant a propósito — si no, una colisión con otro
    // gym escaparía a la comprobación y reventaría como 500 en vez de reintentar / resolverse.
    public Task<MemberInvitation?> GetInvitationByCodeAsync(
        string code, CancellationToken cancellationToken = default)
        => Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Code == code, cancellationToken);

    public Task<bool> InvitationCodeExistsAsync(string code, CancellationToken cancellationToken = default)
        => Invitations.IgnoreQueryFilters().AnyAsync(i => i.Code == code, cancellationToken);

    public Task RemovePendingInvitationsAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Invitations
            .Where(i => i.MemberId == memberId && i.RedeemedAt == null)
            .ExecuteDeleteAsync(cancellationToken);
}
