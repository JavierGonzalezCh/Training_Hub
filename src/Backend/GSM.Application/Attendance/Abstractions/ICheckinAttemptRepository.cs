using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Abstractions;

public sealed record AttemptPageQuery(
    DateOnly? From, DateOnly? To, bool PendingOnly, int Page, int PageSize, CheckinRejectionReason? Reason = null);

public interface ICheckinAttemptRepository
{
    void Add(CheckinAttempt attempt);

    Task<CheckinAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Intento aún sin ver del miembro, de ese motivo, en ese día de negocio, para repetirlo en vez de duplicarlo.</summary>
    Task<CheckinAttempt?> GetPendingForMemberOnDateAsync(
        Guid memberId, DateOnly date, CheckinRejectionReason reason, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CheckinAttempt> Items, int Total)> GetPageAsync(
        AttemptPageQuery query, CancellationToken cancellationToken = default);
}
