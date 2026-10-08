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
            .HasColumnType("routines.equipment[]");

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
