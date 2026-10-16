using GSM.Domain.Achievements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Achievements;

public sealed class RankingParticipantConfiguration : IEntityTypeConfiguration<RankingParticipant>
{
    public void Configure(EntityTypeBuilder<RankingParticipant> builder)
    {
        builder.ToTable("ranking_participants", "achievements");

        builder.Property(p => p.Alias).HasMaxLength(RankingParticipant.AliasMaxLength);

        // Un miembro participa una sola vez.
        builder.HasIndex(p => p.MemberId).IsUnique();
        builder.HasIndex(p => p.GymId);

        // El alias único por gym sin distinguir mayúsculas (índice funcional) va a mano en Scripts/V2_L6.sql.
    }
}
