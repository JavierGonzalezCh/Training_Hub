using GSM.Domain.SuperAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.SuperAdmin;

public sealed class SubscriptionPaymentConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.ToTable("subscription_payments", "superadmin");

        builder.Property(p => p.Amount).HasColumnType("decimal(12,2)");
        builder.Property(p => p.Method).HasColumnName("method");
        builder.Property(p => p.Reference).HasMaxLength(200);
        builder.Property(p => p.Notes).HasMaxLength(1000);

        builder.HasIndex(p => new { p.GymId, p.PaidAt });
        builder.HasIndex(p => p.PaidAt);
    }
}
