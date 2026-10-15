using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Identity;

public sealed class LoginCodeConfiguration : IEntityTypeConfiguration<LoginCode>
{
    public void Configure(EntityTypeBuilder<LoginCode> builder)
    {
        builder.ToTable("login_codes", "identity");

        builder.Property(c => c.CodeHash).HasMaxLength(64).IsRequired();

        // Los límites de petición y la búsqueda del código vigente leen por persona y fecha de emisión.
        // Sin FK a AppUser a propósito: igual que refresh_tokens, la fila no debe frenar un borrado de cuenta.
        builder.HasIndex(c => new { c.UserId, c.IssuedAt });
    }
}
