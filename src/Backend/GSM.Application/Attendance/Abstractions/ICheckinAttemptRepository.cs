using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Abstractions;

public sealed record AttemptPageQuery(DateOnly? From, DateOnly? To, bool PendingOnly, int Page, int PageSize);

public interface ICheckinAttemptRepository
{
    void Add(CheckinAttempt attempt);

    Task<CheckinAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Intento aún sin ver del miembro en ese día de negocio, para repetirlo en vez de duplicarlo.</summary>
    Task<CheckinAttempt?> GetPendingForMemberOnDateAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CheckinAttempt>> GetPendingAsync(CancellationToken cancellationToken = default);

    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CheckinAttempt> Items, int Total)> GetPageAsync(
        AttemptPageQuery query, CancellationToken cancellationToken = default);
}
