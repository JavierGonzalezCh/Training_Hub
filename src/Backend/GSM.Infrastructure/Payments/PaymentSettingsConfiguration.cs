using GSM.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Payments;

public sealed class PaymentSettingsConfiguration : IEntityTypeConfiguration<PaymentSettings>
{
    public void Configure(EntityTypeBuilder<PaymentSettings> builder)
    {
        builder.ToTable("payment_settings", "payments");

        builder.Property(s => s.UseCashDrawer).HasColumnName("use_cash_drawer").HasDefaultValue(true);

        builder.HasIndex(s => s.GymId).IsUnique();
    }
}
