using GSM.Domain.Routines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Routines;

public sealed class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        builder.ToTable("exercises", "routines");

        builder.Property(e => e.Name).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.VideoUrl).HasMaxLength(2048);
        builder.Property(e => e.ImageUrl).HasMaxLength(2048);

        builder.Property(e => e.PrimaryMuscle)
            .HasColumnName("primary_muscle")
            .HasColumnType("routines.muscle");

        builder.Property(e => e.SecondaryMuscles)
            .HasColumnName("secondary_muscles")
            .HasColumnType("routines.muscle[]");

        builder.Property(e => e.Equipment)
            .HasColumnName("equipment")
            .HasColumnType("equipment.equipment_type[]");

        builder.Property(e => e.ExerciseType)
            .HasColumnName("exercise_type")
            .HasColumnType("routines.exercise_type")
            .HasDefaultValue(ExerciseType.Strength);

        builder.Property(e => e.TrackingType)
            .HasColumnName("tracking_type")
            .HasColumnType("routines.tracking_type")
            .HasDefaultValue(TrackingType.RepsWeight);

        builder.Property(e => e.Difficulty)
            .HasColumnName("difficulty")
            .HasColumnType("routines.difficulty")
            .HasDefaultValue(Difficulty.Intermediate);

        builder.Property(e => e.IsUnilateral).HasDefaultValue(false);

        builder.Property(e => e.IsActive).HasDefaultValue(true);

        // Filtrado híbrido explícito (ver ExerciseRepository); sin Global Query Filter.
        builder.HasIndex(e => new { e.GymId, e.Name }).IsUnique();
        builder.HasIndex(e => e.PrimaryMuscle);
    }
}

public sealed class GymHiddenExerciseConfiguration : IEntityTypeConfiguration<GymHiddenExercise>
{
    public void Configure(EntityTypeBuilder<GymHiddenExercise> builder)
    {
        builder.ToTable("gym_hidden_exercises", "routines");

        builder.HasOne<Exercise>()
            .WithMany()
            .HasForeignKey(h => h.ExerciseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(h => new { h.GymId, h.ExerciseId }).IsUnique();
    }
}

public sealed class ExerciseEquipmentModelConfiguration : IEntityTypeConfiguration<ExerciseEquipmentModel>
{
    public void Configure(EntityTypeBuilder<ExerciseEquipmentModel> builder)
    {
        builder.ToTable("exercise_equipment_models", "routines");

        // ModelId apunta a equipment.equipment_models: sin FK entre schemas de módulos distintos.
        builder.HasOne<Exercise>()
            .WithMany()
            .HasForeignKey(l => l.ExerciseId)
            .HasConstraintName("fk_exercise_equipment_models_exercises")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.ExerciseId, l.ModelId }).IsUnique();
        builder.HasIndex(l => l.ModelId);
    }
}

public sealed class ExerciseEquipmentItemConfiguration : IEntityTypeConfiguration<ExerciseEquipmentItem>
{
    public void Configure(EntityTypeBuilder<ExerciseEquipmentItem> builder)
    {
        builder.ToTable("exercise_equipment_items", "routines");

        // EquipmentItemId apunta a equipment.equipment_items: sin FK entre schemas de módulos distintos.
        builder.HasOne<Exercise>()
            .WithMany()
            .HasForeignKey(l => l.ExerciseId)
            .HasConstraintName("fk_exercise_equipment_items_exercises")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.GymId, l.ExerciseId, l.EquipmentItemId }).IsUnique();
        builder.HasIndex(l => new { l.GymId, l.EquipmentItemId });
    }
}
