using GSM.Application.Memberships.Public;
using GSM.Domain.Memberships;

namespace GSM.Application.Memberships;

/// <summary>Un tramo de cobertura de un miembro, tal como está guardado (sin derivar <c>expired</c>).</summary>
public sealed record MembershipSpan(MembershipStatus Status, DateOnly StartDate, DateOnly EndDate);

/// <summary>
/// Calcula el estado comercial y el veredicto de acceso (Memberships v2, §10 y §5.3) a partir de las
/// membresías de un miembro. Puro: sin base de datos ni reloj, para poder probarlo en cada borde.
/// </summary>
public static class CommercialStatusCalculator
{
    private static readonly string[] Months =
        ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    /// <summary>"7 oct" — la forma corta de las fechas en los letreros.</summary>
    public static string ShortDate(DateOnly date) => $"{date.Day} {Months[date.Month - 1]}";

    public static AccessStatus Compute(
        IEnumerable<MembershipSpan> spans, DateOnly today, int graceDays, int expiringDays)
    {
        // Una cancelada no cuenta: deja de cubrir, no da gracia y libera su hueco.
        var live = spans
            .Where(s => s.Status is MembershipStatus.Active or MembershipStatus.Paused)
            .ToList();

        if (live.Count == 0)
        {
            return Blocked(CommercialStatus.NoPlan, AccessBlockReason.NoMembership, null, "No tiene una membresía.");
        }

        // Una membresía congelada no cubre ni da gracia mientras está congelada.
        if (live.FirstOrDefault(s => s.Status == MembershipStatus.Paused) is { } paused)
        {
            return Blocked(CommercialStatus.Frozen, AccessBlockReason.Frozen, paused.EndDate, "Su membresía está congelada.");
        }

        var started = live.Where(s => s.StartDate <= today).ToList();
        if (started.Count == 0)
        {
            // Solo tiene una que empieza más adelante: hoy no está cubierto.
            return Blocked(CommercialStatus.NoPlan, AccessBlockReason.NoMembership, null, "No tiene una membresía vigente.");
        }

        // Con renovaciones encadenadas, lo que cuenta es el fin de la última: quien ya renovó no está "por vencer".
        var end = started.Max(s => s.EndDate);
        if (started.Any(s => s.StartDate <= today && today <= s.EndDate))
        {
            end = live.Max(s => s.EndDate);
            var daysLeft = end.DayNumber - today.DayNumber;
            if (daysLeft <= expiringDays)
            {
                return new AccessStatus(
                    CommercialStatus.Expiring, AccessKind.Expiring, null, end, daysLeft, null, null,
                    ExpiringNotice(end, daysLeft), null);
            }

            return new AccessStatus(
                CommercialStatus.Current, AccessKind.Covered, null, end, daysLeft, null, null, null, null);
        }

        var graceEnd = end.AddDays(graceDays);
        if (today <= graceEnd)
        {
            return new AccessStatus(
                CommercialStatus.Grace, AccessKind.Grace, null, end, null, graceEnd,
                graceEnd.DayNumber - today.DayNumber,
                $"Tu plan venció el {ShortDate(end)}. Tienes hasta el {ShortDate(graceEnd)} para renovar sin perder tu fecha; después no podrás entrar.",
                null);
        }

        return Blocked(
            CommercialStatus.Expired, AccessBlockReason.Expired, end,
            $"Membresía vencida el {ShortDate(end)}, fuera de la gracia.");
    }

    private static string ExpiringNotice(DateOnly end, int daysLeft)
        => daysLeft <= 0
            ? "Tu plan vence hoy."
            : $"Tu plan vence en {daysLeft} {(daysLeft == 1 ? "día" : "días")} (el {ShortDate(end)}). Renueva en recepción para no perder tu fecha.";

    private static AccessStatus Blocked(
        CommercialStatus commercial, AccessBlockReason reason, DateOnly? endsOn, string message)
        => new(commercial, AccessKind.Blocked, reason, endsOn, null, null, null, null, message);
}
