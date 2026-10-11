using GSM.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Members;

public sealed class ConsentDocumentConfiguration : IEntityTypeConfiguration<ConsentDocument>
{
    public void Configure(EntityTypeBuilder<ConsentDocument> builder)
    {
        builder.ToTable("consent_documents", "members");

        builder.Property(d => d.Type).IsRequired();
        builder.Property(d => d.Text).HasMaxLength(ConsentDocument.MaxTextLength).IsRequired();

        // Una versión por tipo y gym: publicar es siempre una fila nueva.
        builder.HasIndex(d => new { d.GymId, d.Type, d.Version }).IsUnique();
    }
}

public sealed class MemberConsentConfiguration : IEntityTypeConfiguration<MemberConsent>
{
    public void Configure(EntityTypeBuilder<MemberConsent> builder)
    {
        builder.ToTable("member_consents", "members");

        builder.Property(c => c.Type).IsRequired();
        builder.Property(c => c.Channel).IsRequired();
        builder.Ignore(c => c.IsRevoked);

        builder.HasIndex(c => new { c.MemberId, c.Type, c.AcceptedAt });
        builder.HasIndex(c => c.GymId);

        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(c => c.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
