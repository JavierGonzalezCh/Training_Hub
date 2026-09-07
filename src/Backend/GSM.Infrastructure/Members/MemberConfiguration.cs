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
        builder.Property(m => m.Phone).HasMaxLength(20);
        builder.Property(m => m.Email).HasMaxLength(256);
        builder.Property(m => m.EmergencyContact).HasMaxLength(200);
        builder.Property(m => m.PhotoUrl).HasMaxLength(2048);
        builder.Property(m => m.QrCode).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Status).HasColumnName("status");

        builder.HasIndex(m => new { m.GymId, m.DocumentNumber }).IsUnique();
        builder.HasIndex(m => new { m.GymId, m.QrCode }).IsUnique();
        builder.HasIndex(m => new { m.GymId, m.Status });
        builder.HasIndex(m => m.UserId);
    }
}
