using GSM.Domain.Achievements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Achievements;

public sealed class RankingMonthResultConfiguration : IEntityTypeConfiguration<RankingMonthResult>
{
    public void Configure(EntityTypeBuilder<RankingMonthResult> builder)
    {
        builder.ToTable("ranking_month_results", "achievements");

        builder.Property(r => r.Month).HasColumnType("date");

        // Un cierre por gym, mes y miembro: repetir el cierre no duplica resultados.
        builder.HasIndex(r => new { r.GymId, r.Month, r.MemberId }).IsUnique();

        // El mejor puesto histórico de un miembro (logros de ranking).
        builder.HasIndex(r => r.MemberId);
    }
}
