using GSM.Domain.Gyms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Gyms;

public sealed class GymOpeningSlotConfiguration : IEntityTypeConfiguration<GymOpeningSlot>
{
    public void Configure(EntityTypeBuilder<GymOpeningSlot> builder)
    {
        builder.ToTable("gym_opening_slots", "gyms", t =>
        {
            t.HasCheckConstraint("ck_gym_opening_slots_day", "day BETWEEN 0 AND 7");
            t.HasCheckConstraint("ck_gym_opening_slots_order", "opens_at < closes_at");
        });

        builder.Property(s => s.Day).HasConversion<int>().IsRequired();
        builder.Property(s => s.OpensAt).IsRequired();
        builder.Property(s => s.ClosesAt).IsRequired();
        builder.Ignore(s => s.Slot);

        builder.HasIndex(s => new { s.GymId, s.Day }).HasDatabaseName("ix_gym_opening_slots_gym_id_day");

        builder.HasOne<Gym>()
            .WithMany()
            .HasForeignKey(s => s.GymId)
            .HasConstraintName("fk_gym_opening_slots_gyms")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GymSpecialDayConfiguration : IEntityTypeConfiguration<GymSpecialDay>
{
    public void Configure(EntityTypeBuilder<GymSpecialDay> builder)
    {
        builder.ToTable("gym_special_days", "gyms");

        builder.Property(d => d.Date).IsRequired();
        builder.Property(d => d.Note).HasMaxLength(GymSpecialDay.MaxNoteLength);
        builder.Ignore(d => d.TimeSlots);

        builder.HasIndex(d => new { d.GymId, d.Date }).IsUnique().HasDatabaseName("ix_gym_special_days_gym_id_date");

        builder.HasOne<Gym>()
            .WithMany()
            .HasForeignKey(d => d.GymId)
            .HasConstraintName("fk_gym_special_days_gyms")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Slots)
            .WithOne()
            .HasForeignKey(s => s.SpecialDayId)
            .HasConstraintName("fk_gym_special_day_slots_gym_special_days")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(d => d.Slots).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class GymSpecialDaySlotConfiguration : IEntityTypeConfiguration<GymSpecialDaySlot>
{
    public void Configure(EntityTypeBuilder<GymSpecialDaySlot> builder)
    {
        builder.ToTable("gym_special_day_slots", "gyms", t =>
            t.HasCheckConstraint("ck_gym_special_day_slots_order", "opens_at < closes_at"));

        builder.Property(s => s.OpensAt).IsRequired();
        builder.Property(s => s.ClosesAt).IsRequired();
        builder.Ignore(s => s.Slot);

        builder.HasIndex(s => s.SpecialDayId).HasDatabaseName("ix_gym_special_day_slots_special_day_id");
    }
}
