using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Application.Attendance.Abstractions;

public sealed record AttendancePageQuery(
    Guid? MemberId, DateOnly? From, DateOnly? To, int Page, int PageSize, bool AuthorizedOnly = false);

public interface IAttendanceRepository
{
    void Add(AttendanceEntity attendance);

    Task<AttendanceEntity?> GetForMemberOnDateAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Todas las entradas del miembro ese día de negocio, de la más antigua a la más reciente (A1b).</summary>
    Task<IReadOnlyList<AttendanceEntity>> GetEntriesOnDateAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<AttendanceEntity> Items, int Total)> GetPageAsync(
        AttendancePageQuery query, CancellationToken cancellationToken = default);

    /// <summary>Fechas de asistencia distintas del miembro, para el cálculo de racha.</summary>
    Task<IReadOnlyList<DateOnly>> GetMemberDatesAsync(
        Guid memberId, CancellationToken cancellationToken = default);

    Task<int> GetVisitCountAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<int> GetTodayCountAsync(Guid gymId, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Asistencias del gym con <c>checked_in_at</c> en <c>[fromUtc, toUtc)</c> (horas pico).</summary>
    Task<IReadOnlyList<DateTimeOffset>> GetCheckInInstantsAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
