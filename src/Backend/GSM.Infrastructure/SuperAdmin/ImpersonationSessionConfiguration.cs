using GSM.Domain.SuperAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.SuperAdmin;

public sealed class ImpersonationSessionConfiguration : IEntityTypeConfiguration<ImpersonationSession>
{
    public void Configure(EntityTypeBuilder<ImpersonationSession> builder)
    {
        builder.ToTable("impersonation_sessions", "superadmin");

        builder.Property(s => s.Reason).HasMaxLength(ImpersonationSession.MaxReasonLength).IsRequired();

        builder.HasIndex(s => new { s.GymId, s.StartedAt });
        builder.HasIndex(s => new { s.SuperAdminUserId, s.StartedAt });
    }
}
