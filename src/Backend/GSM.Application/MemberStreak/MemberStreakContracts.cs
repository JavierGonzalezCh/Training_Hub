namespace GSM.Application.MemberStreak;

public sealed record MemberStreakResponse(
    Guid MemberId,
    int CurrentStreak,
    int TotalVisits,
    DateOnly? LastVisit)
{
    /// <summary>Congeladores disponibles (etapa 9.5). 0 mientras el saldo no aplique.</summary>
    public int FreezesAvailable { get; init; }
}
