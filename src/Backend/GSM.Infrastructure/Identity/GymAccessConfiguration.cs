using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Identity;

public sealed class GymAccessConfiguration : IEntityTypeConfiguration<GymAccess>
{
    public void Configure(EntityTypeBuilder<GymAccess> builder)
    {
        builder.ToTable("gym_accesses", "identity");

        builder.Property(a => a.Role).HasColumnName("role");
        builder.Property(a => a.IsActive).HasDefaultValue(true);

        // Un acceso de equipo y uno de miembro por persona y gym, como máximo.
        builder.HasIndex(a => new { a.UserId, a.GymId, a.IsTeam }).IsUnique();
        builder.HasIndex(a => new { a.GymId, a.Role });

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
