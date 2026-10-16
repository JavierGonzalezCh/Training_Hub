using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Identity;

public sealed class EmailChangeRequestConfiguration : IEntityTypeConfiguration<EmailChangeRequest>
{
    public void Configure(EntityTypeBuilder<EmailChangeRequest> builder)
    {
        builder.ToTable("email_change_requests", "identity");

        builder.Property(r => r.NewEmail).HasMaxLength(256).IsRequired();
        builder.Property(r => r.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.CurrentCodeHash).HasMaxLength(64).IsRequired();

        // Los límites de petición y la búsqueda del pedido vigente leen por persona y fecha de emisión.
        // Sin FK a AppUser a propósito (igual que login_codes y refresh_tokens).
        builder.HasIndex(r => new { r.UserId, r.IssuedAt });
    }
}
