using GSM.Domain.Attendance;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Application.Attendance;

/// <summary>
/// ¿Puede el miembro registrar otra entrada hoy? (Attendance v2, §3.1 y §3.2). Regla general: una por día.
/// Con una <see cref="MemberEntryRule"/> vigente: hasta su máximo, con 2 horas mínimo entre entradas.
/// Función pura, la comparten el registro y la tarjeta de recepción.
/// </summary>
public sealed record EntryAllowance(
    bool Allowed,
    int EntriesToday,
    int AllowedToday,
    DateTimeOffset? LastEntryAt,
    DateTimeOffset? NextEntryAt)
{
    /// <summary>Hay una regla vigente que permite más de una entrada.</summary>
    public bool MultipleEntries => AllowedToday > 1;

    public static EntryAllowance Evaluate(
        IReadOnlyList<AttendanceEntity> entriesToday, MemberEntryRule? rule, DateOnly today, DateTimeOffset now)
    {
        var allowedToday = rule?.AllowedEntriesOn(today) ?? 1;
        var last = entriesToday.Count == 0 ? (DateTimeOffset?)null : entriesToday.Max(e => e.CheckedInAt);

        if (entriesToday.Count == 0)
        {
            return new EntryAllowance(true, 0, allowedToday, null, null);
        }

        if (entriesToday.Count >= allowedToday)
        {
            return new EntryAllowance(false, entriesToday.Count, allowedToday, last, null);
        }

        var next = last!.Value + MemberEntryRule.MinGapBetweenEntries;
        return now >= next
            ? new EntryAllowance(true, entriesToday.Count, allowedToday, last, null)
            : new EntryAllowance(false, entriesToday.Count, allowedToday, last, next);
    }
}
