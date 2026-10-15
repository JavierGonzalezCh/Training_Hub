using GSM.Application.Communications.Abstractions;
using GSM.Domain.Communications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Communications;

public sealed class CommunicationsSettingsConfiguration : IEntityTypeConfiguration<CommunicationsSettings>
{
    public void Configure(EntityTypeBuilder<CommunicationsSettings> builder)
    {
        builder.ToTable("settings", "communications");
        builder.HasIndex(s => s.GymId).IsUnique();
        builder.Property(s => s.BirthdayEnabled).HasDefaultValue(false);
        builder.Property(s => s.BirthdayMessage).HasMaxLength(CommunicationsSettings.BirthdayMessageMaxLength);
    }
}

public sealed class CommunicationsSettingsRepository(GsmDbContext dbContext) : ICommunicationsSettingsRepository
{
    private DbSet<CommunicationsSettings> Settings => dbContext.Set<CommunicationsSettings>();

    public Task<CommunicationsSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Settings.FirstOrDefaultAsync(s => s.GymId == gymId, cancellationToken);

    public void Add(CommunicationsSettings settings) => Settings.Add(settings);

    public async Task<IReadOnlyList<CommunicationsSettings>> ListBirthdayEnabledAsync(
        CancellationToken cancellationToken = default)
        => await Settings.AsNoTracking().Where(s => s.BirthdayEnabled).ToListAsync(cancellationToken);
}
