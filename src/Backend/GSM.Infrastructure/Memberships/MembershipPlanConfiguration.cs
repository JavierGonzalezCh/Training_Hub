using GSM.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Memberships;

public sealed class MembershipPlanConfiguration : IEntityTypeConfiguration<MembershipPlan>
{
    public void Configure(EntityTypeBuilder<MembershipPlan> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.Price).HasColumnType("decimal(12,2)");

        // MS3: la duración se expresa en días o en meses del calendario. La columna antigua
        // duration_days se renombra a duration_value (los planes existentes quedan en 'days').
        builder.Property(p => p.DurationValue).HasColumnName("duration_value");
        builder.Property(p => p.DurationUnit).HasColumnName("duration_unit").HasDefaultValue(DurationUnit.Days);
        builder.Property(p => p.IsActive).HasDefaultValue(true);

        // MS4: reglas de congelamiento por plan (0 días = no se congela; mínimo por vez).
        builder.Property(p => p.MaxFreezeDays).HasColumnName("max_freeze_days").HasDefaultValue(0);
        builder.Property(p => p.MinFreezeDays).HasColumnName("min_freeze_days").HasDefaultValue(1);

        // MS5: tiqueteras. 'time' por defecto (los planes de antes); la vigencia de una tiquetera es duration_value en días.
        builder.Property(p => p.Kind).HasColumnName("kind").HasDefaultValue(PlanKind.Time);
        builder.Property(p => p.Entries).HasColumnName("entries");
        builder.Ignore(p => p.ValidityDays);
        builder.Ignore(p => p.IsPass);

        // MS7: cupos. 1 = individual (los planes de antes); más de 1 = plan de grupo, siempre por tiempo.
        builder.Property(p => p.Seats).HasColumnName("seats").HasDefaultValue(1);
        builder.Ignore(p => p.IsGroup);

        builder.ToTable("membership_plans", "memberships", t =>
        {
            t.HasCheckConstraint("ck_membership_plans_pass_entries", "(kind = 'pass') = (entries IS NOT NULL)");
            t.HasCheckConstraint("ck_membership_plans_seats", "seats >= 1 AND seats <= 500");
            t.HasCheckConstraint("ck_membership_plans_group_is_time", "seats = 1 OR kind = 'time'");
        });

        builder.HasIndex(p => new { p.GymId, p.Name }).IsUnique();
    }
}
