using GSM.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Payments;

public sealed class VoidRequestConfiguration : IEntityTypeConfiguration<VoidRequest>
{
    public void Configure(EntityTypeBuilder<VoidRequest> builder)
    {
        builder.ToTable("void_requests", "payments");

        builder.Property(r => r.Status).HasColumnName("status");
        builder.Property(r => r.Reason).HasMaxLength(VoidRequest.ReasonMaxLength).IsRequired();
        builder.Property(r => r.RejectionNote).HasMaxLength(VoidRequest.ReasonMaxLength);

        // Un pago tiene a lo sumo una solicitud pendiente: dos pedidos simultáneos chocan aquí.
        builder.HasIndex(r => r.PaymentId)
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ux_void_requests_pending_payment");
        builder.HasIndex(r => new { r.GymId, r.Status, r.RequestedAt });
    }
}
