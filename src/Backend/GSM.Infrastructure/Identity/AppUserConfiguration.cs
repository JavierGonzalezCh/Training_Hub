using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Identity;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public const string SingleSuperAdminIndex = "ux_users_single_super_admin";

    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        // La tabla y el schema se fijan en GsmDbContext.OnModelCreating.
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.IsSuperAdmin).HasDefaultValue(false);
        builder.Property(u => u.IsActive).HasDefaultValue(true);
        builder.Property(u => u.MustChangePassword).HasDefaultValue(false);

        // Solo una cuenta puede ser SuperAdmin (V5): índice único parcial.
        builder.HasIndex(u => u.IsSuperAdmin)
            .IsUnique()
            .HasDatabaseName(SingleSuperAdminIndex)
            .HasFilter("is_super_admin");

        builder.HasMany(u => u.RefreshTokens)
            .WithOne()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
