using GSM.Domain.Routines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Routines;

public sealed class RoutineExerciseLogConfiguration : IEntityTypeConfiguration<RoutineExerciseLog>
{
    public void Configure(EntityTypeBuilder<RoutineExerciseLog> builder)
    {
        builder.ToTable("routine_exercise_logs", "routines");

        builder.Property(l => l.RepsDone).HasMaxLength(30);
        builder.Property(l => l.Notes).HasMaxLength(500);
        builder.Property(l => l.WeightKg).HasPrecision(6, 2);

        // Un registro por ejercicio por día: repetir actualiza.
        builder.HasIndex(l => new { l.RoutineExerciseId, l.PerformedOn }).IsUnique();
        builder.HasIndex(l => new { l.MemberId, l.PerformedOn });

        builder.HasOne<RoutineExercise>()
            .WithMany()
            .HasForeignKey(l => l.RoutineExerciseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
