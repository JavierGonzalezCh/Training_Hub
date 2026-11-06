using GSM.Domain.Routines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Routines;

public sealed class RoutineDayOverrideConfiguration : IEntityTypeConfiguration<RoutineDayOverride>
{
    public void Configure(EntityTypeBuilder<RoutineDayOverride> builder)
    {
        builder.ToTable("routine_day_overrides", "routines");

        // Un ajuste por miembro y fecha: lo que cuenta ese día es uno solo.
        builder.HasIndex(o => new { o.MemberId, o.Date }).IsUnique();

        builder.HasOne<Routine>()
            .WithMany()
            .HasForeignKey(o => o.RoutineId)
            .OnDelete(DeleteBehavior.Cascade);

        // Los días con historial se retiran, no se borran. NO ACTION (no RESTRICT): se comprueba al final de la sentencia,
        // así borrar una rutina entera (cascada a días y ajustes) no choca.
        builder.HasOne<RoutineDay>()
            .WithMany()
            .HasForeignKey(o => o.PlannedDayId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<RoutineDay>()
            .WithMany()
            .HasForeignKey(o => o.PerformedDayId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
