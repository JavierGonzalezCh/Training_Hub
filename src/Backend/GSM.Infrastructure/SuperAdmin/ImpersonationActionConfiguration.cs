using GSM.Domain.SuperAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.SuperAdmin;

public sealed class ImpersonationActionConfiguration : IEntityTypeConfiguration<ImpersonationAction>
{
    public void Configure(EntityTypeBuilder<ImpersonationAction> builder)
    {
        builder.ToTable("impersonation_actions", "superadmin");

        builder.Property(a => a.Method).HasMaxLength(ImpersonationAction.MaxMethodLength).IsRequired();
        builder.Property(a => a.Endpoint).HasMaxLength(ImpersonationAction.MaxEndpointLength).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(ImpersonationAction.MaxSummaryLength).IsRequired();

        builder.HasOne<ImpersonationSession>()
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.SessionId, a.OccurredAt });
    }
}
