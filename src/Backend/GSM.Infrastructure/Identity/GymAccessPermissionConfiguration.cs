using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Identity;

public sealed class GymAccessPermissionConfiguration : IEntityTypeConfiguration<GymAccessPermission>
{
    public void Configure(EntityTypeBuilder<GymAccessPermission> builder)
    {
        builder.ToTable("gym_access_permissions", "identity");

        builder.Property(p => p.Permission).HasColumnName("permission");
        builder.Property(p => p.Granted).HasColumnName("granted");

        // Un ajuste por permiso y acceso.
        builder.HasIndex(p => new { p.GymAccessId, p.Permission }).IsUnique();

        builder.HasOne<GymAccess>()
            .WithMany()
            .HasForeignKey(p => p.GymAccessId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StaffAccessAuditConfiguration : IEntityTypeConfiguration<StaffAccessAudit>
{
    public void Configure(EntityTypeBuilder<StaffAccessAudit> builder)
    {
        builder.ToTable("staff_access_audits", "identity");

        builder.Property(a => a.Action).HasColumnName("action");
        builder.Property(a => a.Permission).HasColumnName("permission");
        builder.Property(a => a.OldValue).HasMaxLength(50);
        builder.Property(a => a.NewValue).HasMaxLength(50);

        builder.HasIndex(a => new { a.GymId, a.TargetUserId, a.CreatedAt });

        // Sin FK al acceso: la auditoría sobrevive aunque el acceso se borre algún día.
    }
}
