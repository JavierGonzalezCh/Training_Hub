namespace GSM.Application.Achievements.Contracts;

/// <summary>Acepta (o cambia cómo) aparecer en el ranking. <c>UseAlias = false</c>: nombre e inicial del apellido con foto.</summary>
public sealed record JoinRankingRequest(bool UseAlias, string? Alias);

/// <summary>Una fila del ranking. Con alias no hay foto ni apellido.</summary>
public sealed record RankingEntryResponse(int Position, string DisplayName, string? PhotoUrl, int Days, bool IsMe);

/// <summary>El estado del propio miembro frente al ranking. <c>Position</c> nulo: no participa o aún no suma días este mes.</summary>
public sealed record RankingMeResponse(
    bool Participating,
    bool UsesAlias,
    string? Alias,
    int? Position,
    int Days,
    bool CanParticipate,
    string? CannotParticipateReason);

/// <summary>
/// Ranking mensual de constancia (spec §5): top 10 del mes en curso, la posición propia y el podio del mes anterior.
/// Mes y año van separados para que el cliente arme "octubre de 2026" en su idioma.
/// </summary>
public sealed record RankingResponse(
    int Year,
    int Month,
    IReadOnlyList<RankingEntryResponse> Top,
    RankingMeResponse Me,
    int PreviousYear,
    int PreviousMonth,
    IReadOnlyList<RankingEntryResponse> PreviousPodium);
