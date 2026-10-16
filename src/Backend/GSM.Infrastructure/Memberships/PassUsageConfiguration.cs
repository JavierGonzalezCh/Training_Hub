using GSM.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Memberships;

/// <summary>MS5: una fila por entrada de tiquetera consumida (Memberships v2, §7).</summary>
public sealed class PassUsageConfiguration : IEntityTypeConfiguration<PassUsage>
{
    public void Configure(EntityTypeBuilder<PassUsage> builder)
    {
        builder.ToTable("pass_usages", "memberships");

        // Una entrada de asistencia consume como máximo una entrada de tiquetera: consumir es idempotente.
        builder.HasIndex(u => u.AttendanceId).IsUnique();
        builder.HasIndex(u => u.MembershipId);

        builder.HasOne<Membership>()
            .WithMany()
            .HasForeignKey(u => u.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
