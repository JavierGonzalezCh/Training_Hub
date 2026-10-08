using GSM.Application.Communications.Abstractions;
using GSM.Domain.Communications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Communications;

public sealed class MemberCommunicationPreferencesConfiguration : IEntityTypeConfiguration<MemberCommunicationPreferences>
{
    public void Configure(EntityTypeBuilder<MemberCommunicationPreferences> builder)
    {
        builder.ToTable("member_communication_preferences", "communications");
        builder.HasIndex(p => p.MemberId).IsUnique();
    }
}

public sealed class CommunicationsSettingsConfiguration : IEntityTypeConfiguration<CommunicationsSettings>
{
    public void Configure(EntityTypeBuilder<CommunicationsSettings> builder)
    {
        builder.ToTable("settings", "communications");
        builder.HasIndex(s => s.GymId).IsUnique();
    }
}

public sealed class MemberCommunicationPreferencesRepository(GsmDbContext dbContext)
    : IMemberCommunicationPreferencesRepository
{
    private DbSet<MemberCommunicationPreferences> Preferences => dbContext.Set<MemberCommunicationPreferences>();

    public Task<MemberCommunicationPreferences?> GetAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Preferences.FirstOrDefaultAsync(p => p.MemberId == memberId, cancellationToken);

    public void Add(MemberCommunicationPreferences preferences) => Preferences.Add(preferences);

    public async Task<IReadOnlySet<Guid>> GetOptedOutAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await Preferences.AsNoTracking()
            .Where(p => p.EmailOptOut && memberIds.Contains(p.MemberId))
            .Select(p => p.MemberId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }
}

public sealed class CommunicationsSettingsRepository(GsmDbContext dbContext) : ICommunicationsSettingsRepository
{
    private DbSet<CommunicationsSettings> Settings => dbContext.Set<CommunicationsSettings>();

    public Task<CommunicationsSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Settings.FirstOrDefaultAsync(s => s.GymId == gymId, cancellationToken);

    public void Add(CommunicationsSettings settings) => Settings.Add(settings);
}
