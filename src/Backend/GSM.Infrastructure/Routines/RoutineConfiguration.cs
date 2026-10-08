using GSM.Domain.Routines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Routines;

public sealed class RoutineConfiguration : IEntityTypeConfiguration<Routine>
{
    public void Configure(EntityTypeBuilder<Routine> builder)
    {
        builder.ToTable("routines", "routines");

        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.StartsOn).HasColumnType("date");
        builder.Property(r => r.DeactivatedOn).HasColumnType("date");
        builder.Property(r => r.ScheduleMode).HasDefaultValue(GSM.Domain.Routines.ScheduleMode.Rotating);

        builder.HasMany(r => r.Days)
            .WithOne()
            .HasForeignKey(d => d.RoutineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Days).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(r => new { r.GymId, r.MemberId, r.IsActive });
    }
}

public sealed class RoutineDayConfiguration : IEntityTypeConfiguration<RoutineDay>
{
    public void Configure(EntityTypeBuilder<RoutineDay> builder)
    {
        builder.ToTable("routine_days", "routines");

        builder.Property(d => d.Name).HasMaxLength(60);
        builder.Property(d => d.RetiredOn).HasColumnType("date");
        builder.ToTable(t => t.HasCheckConstraint("ck_routine_days_day_of_week", "day_of_week is null or day_of_week between 1 and 7"));

        builder.HasMany(d => d.Exercises)
            .WithOne()
            .HasForeignKey(e => e.RoutineDayId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(d => d.Exercises).UsePropertyAccessMode(PropertyAccessMode.Field);

        // No único: al reordenar, EF actualiza fila por fila y un intercambio de posiciones chocaría a
        // medias; las posiciones de los días vigentes las garantiza el dominio (1..N sin huecos).
        builder.HasIndex(d => new { d.RoutineId, d.Position });
    }
}

public sealed class RoutineExerciseConfiguration : IEntityTypeConfiguration<RoutineExercise>
{
    public void Configure(EntityTypeBuilder<RoutineExercise> builder)
    {
        builder.ToTable("routine_exercises", "routines");

        builder.Property(e => e.TargetReps).HasMaxLength(30).IsRequired();
        builder.Property(e => e.TargetWeightKg).HasPrecision(6, 2);
        builder.Property(e => e.Notes).HasMaxLength(500);
        builder.Property(e => e.RetiredOn).HasColumnType("date");

        builder.HasIndex(e => new { e.RoutineDayId, e.Order });
    }
}
