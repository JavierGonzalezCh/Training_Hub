using GSM.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Members;

public sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("members", "members");

        builder.Property(m => m.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(m => m.LastName).HasMaxLength(100).IsRequired();
        builder.Property(m => m.DocumentNumber).HasMaxLength(30).IsRequired();
        builder.Property(m => m.DocumentType).IsRequired();
        builder.Property(m => m.Phone).HasMaxLength(20);
        builder.Property(m => m.Email).HasMaxLength(256);
        builder.Property(m => m.Address).HasMaxLength(200);
        builder.Property(m => m.City).HasMaxLength(100);
        builder.Property(m => m.PhotoUrl).HasMaxLength(2048);
        builder.Property(m => m.EmergencyContactName).HasMaxLength(150);
        builder.Property(m => m.EmergencyContactRelationship).HasMaxLength(50);
        builder.Property(m => m.EmergencyContactPhone).HasMaxLength(20);
        builder.Property(m => m.GuardianName).HasMaxLength(150);
        builder.Property(m => m.GuardianDocumentNumber).HasMaxLength(30);
        builder.Property(m => m.GuardianPhone).HasMaxLength(20);
        builder.Property(m => m.GuardianRelationship).HasMaxLength(50);
        builder.Property(m => m.Tags).HasColumnType("text[]").IsRequired();
        builder.Property(m => m.Status).HasColumnName("status");

        builder.Ignore(m => m.IsProfileIncomplete);

        // Un mismo número puede repetirse con otro tipo de documento (cc 123 y ti 123).
        builder.HasIndex(m => new { m.GymId, m.DocumentType, m.DocumentNumber }).IsUnique();
        builder.HasIndex(m => new { m.GymId, m.Status });
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.ReferredByMemberId);
        builder.HasIndex(m => m.Tags).HasMethod("gin");

        // Mismo schema, así que la FK es válida. El gym lo comprueba el caso de uso (otro miembro del gym).
        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(m => m.ReferredByMemberId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class MemberChangeConfiguration : IEntityTypeConfiguration<MemberChange>
{
    public void Configure(EntityTypeBuilder<MemberChange> builder)
    {
        builder.ToTable("member_changes", "members");

        builder.Property(c => c.Field).HasMaxLength(60).IsRequired();
        builder.Property(c => c.OldValue).HasMaxLength(2000);
        builder.Property(c => c.NewValue).HasMaxLength(2000);

        builder.HasIndex(c => new { c.MemberId, c.ChangedAt });
        builder.HasIndex(c => c.GymId);

        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(c => c.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MemberNoteConfiguration : IEntityTypeConfiguration<MemberNote>
{
    public void Configure(EntityTypeBuilder<MemberNote> builder)
    {
        builder.ToTable("member_notes", "members");

        builder.Property(n => n.Text).HasMaxLength(MemberNote.MaxLength).IsRequired();

        builder.HasIndex(n => new { n.MemberId, n.CreatedAt });
        builder.HasIndex(n => n.GymId);

        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(n => n.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
