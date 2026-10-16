namespace GSM.Application.Achievements.Public;

/// <summary>Lectura del ranking de constancia que Achievements expone a los compositores (D2).</summary>
public interface IRankingStandingReader
{
    /// <summary>
    /// Dónde está el miembro en el ranking del mes. Nulo si el gym apagó el ranking; con
    /// <c>Participating = false</c> si no aceptó aparecer (entonces no hay posición).
    /// </summary>
    Task<RankingStandingView?> GetAsync(Guid memberId, CancellationToken cancellationToken = default);
}

/// <param name="Position">Puesto en el mes en curso; nulo si no participa o aún no tiene días en el mes.</param>
/// <param name="Days">Días con entrada en el mes en curso.</param>
public sealed record RankingStandingView(bool Participating, int? Position, int Days);
