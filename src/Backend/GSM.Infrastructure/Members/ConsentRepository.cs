using GSM.Application.Members.Abstractions;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Members;

public sealed class ConsentRepository(GsmDbContext dbContext) : IConsentRepository
{
    private DbSet<ConsentDocument> Documents => dbContext.Set<ConsentDocument>();

    private DbSet<MemberConsent> Consents => dbContext.Set<MemberConsent>();

    public Task<ConsentDocument?> GetLatestDocumentAsync(ConsentType type, CancellationToken cancellationToken = default)
        => Documents.Where(d => d.Type == type)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(ConsentDocument document) => Documents.Add(document);

    public async Task<IReadOnlyList<MemberConsent>> GetForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await Consents.Where(c => c.MemberId == memberId)
            .OrderByDescending(c => c.AcceptedAt).ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);

    public Task<MemberConsent?> GetLatestAsync(Guid memberId, ConsentType type, CancellationToken cancellationToken = default)
        => Consents.Where(c => c.MemberId == memberId && c.Type == type)
            .OrderByDescending(c => c.AcceptedAt).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(MemberConsent consent) => Consents.Add(consent);
}
