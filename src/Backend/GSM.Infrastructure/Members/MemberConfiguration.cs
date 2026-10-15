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

        // Salud (M3): datos sensibles; nulos cuando están vacíos (HasHealthAlerts se deriva de eso).
        builder.Property(m => m.MedicalConditions).HasMaxLength(Member.HealthTextMaxLength);
        builder.Property(m => m.Medications).HasMaxLength(Member.HealthTextMaxLength);
        builder.Property(m => m.TrainingRestrictions).HasMaxLength(Member.HealthTextMaxLength);

        builder.Ignore(m => m.IsProfileIncomplete);
        builder.Ignore(m => m.HasHealthAlerts);

        // Un mismo número puede repetirse con otro tipo de documento (cc 123 y ti 123).
        builder.HasIndex(m => new { m.GymId, m.DocumentType, m.DocumentNumber }).IsUnique();
        builder.HasIndex(m => new { m.GymId, m.Status });
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.ReferredByMemberId);

        // Sin FK: el entrenador es una cuenta de Identity (otro schema). "Mis miembros" filtra por esta columna.
        builder.HasIndex(m => new { m.GymId, m.TrainerUserId });
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

public sealed class MemberHealthAccessConfiguration : IEntityTypeConfiguration<MemberHealthAccess>
{
    public void Configure(EntityTypeBuilder<MemberHealthAccess> builder)
    {
        builder.ToTable("health_access_log", "members");

        builder.Property(a => a.Role).HasMaxLength(20).IsRequired();

        // Una fila por usuario, miembro y día (§10). El repositorio inserta con ON CONFLICT sobre este índice.
        builder.HasIndex(a => new { a.MemberId, a.UserId, a.AccessedOn }).IsUnique();
        builder.HasIndex(a => new { a.MemberId, a.AccessedAt });
        builder.HasIndex(a => a.GymId);

        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(a => a.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MemberNoteConfiguration: IEntityTypeConfiguration<MemberNote>
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

public sealed class MemberMeasurementConfiguration : IEntityTypeConfiguration<MemberMeasurement>
{
    public void Configure(EntityTypeBuilder<MemberMeasurement> builder)
    {
        builder.ToTable("member_measurements", "members", t =>
        {
            t.HasCheckConstraint("ck_member_measurements_weight_kg", "weight_kg >= 20 AND weight_kg <= 400");
            t.HasCheckConstraint("ck_member_measurements_height_cm", "height_cm IS NULL OR (height_cm >= 50 AND height_cm <= 250)");
        });

        builder.Property(m => m.WeightKg).HasPrecision(5, 2);
        builder.Property(m => m.HeightCm).HasPrecision(5, 1);
        builder.Property(m => m.RecordedByRole).HasMaxLength(20).IsRequired();

        builder.HasIndex(m => new { m.MemberId, m.MeasuredOn });
        builder.HasIndex(m => m.GymId);

        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(m => m.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
