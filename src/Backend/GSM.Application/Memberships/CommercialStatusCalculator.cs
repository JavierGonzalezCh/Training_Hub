using GSM.Application.Memberships.Public;
using GSM.Domain.Memberships;

namespace GSM.Application.Memberships;

/// <summary>Un congelamiento tal como lo ve el calculador: <paramref name="EndsOn"/> nulo = abierto.</summary>
public sealed record FreezeSpan(DateOnly StartsOn, DateOnly? EndsOn)
{
    public bool CoversDate(DateOnly date) => StartsOn <= date && (EndsOn is null || date <= EndsOn);
}

/// <summary>
/// Un tramo de cobertura de un miembro, tal como está guardado (sin derivar <c>expired</c>), con sus
/// congelamientos (MS4): un día dentro de uno no está cubierto, y eso se calcula.
/// </summary>
/// <param name="EntriesTotal">Entradas con que empezó, si es una tiquetera (MS5); nulo en una membresía por tiempo.</param>
/// <param name="EntriesUsed">Entradas ya consumidas (solo tiqueteras).</param>
public sealed record MembershipSpan(
    MembershipStatus Status, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<FreezeSpan>? Freezes = null,
    int? EntriesTotal = null, int EntriesUsed = 0)
{
    public bool IsPass => EntriesTotal is not null;

    /// <summary>Entradas que le quedan (0 si ya no tiene). Solo tiqueteras.</summary>
    public int EntriesLeft => Math.Max(0, (EntriesTotal ?? 0) - EntriesUsed);

    /// <summary>
    /// ¿Está congelada ese día? <see cref="MembershipStatus.Paused"/> es el formato anterior a MS4
    /// (ya no se guarda; la migración lo convierte en un congelamiento abierto).
    /// </summary>
    public bool IsFrozenOn(DateOnly date)
        => Status == MembershipStatus.Paused || (Freezes?.Any(f => f.CoversDate(date)) ?? false);

    /// <summary>Último día del congelamiento que cubre el día, si es programado (nulo si es abierto o no hay).</summary>
    public DateOnly? FrozenUntil(DateOnly date)
        => Freezes?.FirstOrDefault(f => f.CoversDate(date))?.EndsOn;
}

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

    /// <summary>A una tiquetera le quedan pocas entradas con 2 o menos (§5.3): sale el letrero y cuenta como "por vencer".</summary>
    public const int PassLowEntries = 2;

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
        if (live.FirstOrDefault(s => s.IsFrozenOn(today)) is { } frozen)
        {
            var message = frozen.FrozenUntil(today) is { } until
                ? $"Su membresía está congelada hasta el {ShortDate(until)}."
                : "Su membresía está congelada.";
            return Blocked(CommercialStatus.Frozen, AccessBlockReason.Frozen, frozen.EndDate, message);
        }

        var passes = live.Where(s => s.IsPass).ToList();
        if (passes.Count == 0)
        {
            return ComputeTime(live, today, graceDays, expiringDays);
        }

        var time = live.Where(s => !s.IsPass).ToList();
        var timeResult = time.Count == 0 ? null : ComputeTime(time, today, graceDays, expiringDays);
        if (timeResult is { Kind: AccessKind.Covered or AccessKind.Expiring })
        {
            // Una membresía por tiempo que cubre hoy manda: la tiquetera no se toca.
            return timeResult;
        }

        // Entre varias tiqueteras vigentes con entradas se usa la que vence primero.
        var usable = passes
            .Where(p => p.StartDate <= today && today <= p.EndDate && p.EntriesLeft > 0)
            .OrderBy(p => p.EndDate)
            .FirstOrDefault();
        if (usable is not null)
        {
            return PassAccess(usable, today, expiringDays);
        }

        // Las tiqueteras no dan gracia, pero la gracia de una membresía por tiempo sí vale (§5.1).
        if (timeResult is { Kind: AccessKind.Grace })
        {
            return timeResult;
        }

        var covering = passes
            .Where(p => p.StartDate <= today && today <= p.EndDate)
            .OrderByDescending(p => p.EndDate)
            .FirstOrDefault();
        if (covering is not null)
        {
            // Vigente pero sin entradas: sin gracia por entradas.
            return new AccessStatus(
                CommercialStatus.Expired, AccessKind.Blocked, AccessBlockReason.PassExhausted, covering.EndDate,
                null, null, null, null, "Ya usó todas las entradas de su tiquetera.",
                EntriesLeft: 0, EntriesTotal: covering.EntriesTotal);
        }

        if (timeResult is not null)
        {
            return timeResult;
        }

        var nextPass = passes.Where(p => p.StartDate > today).Select(p => (DateOnly?)p.StartDate).Min();
        if (nextPass is { } next)
        {
            return Scheduled(next);
        }

        // Todas vencidas por fecha: sin gracia.
        var lastEnd = passes.Max(p => p.EndDate);
        return Blocked(
            CommercialStatus.Expired, AccessBlockReason.Expired, lastEnd,
            $"Su tiquetera venció el {ShortDate(lastEnd)} (las tiqueteras no tienen gracia).");
    }

    private static AccessStatus PassAccess(MembershipSpan pass, DateOnly today, int expiringDays)
    {
        var daysLeft = pass.EndDate.DayNumber - today.DayNumber;
        var left = pass.EntriesLeft;
        var expiring = daysLeft <= expiringDays || left <= PassLowEntries;
        return new AccessStatus(
            expiring ? CommercialStatus.Expiring : CommercialStatus.Current,
            expiring ? AccessKind.Expiring : AccessKind.Covered,
            null, pass.EndDate, daysLeft, null, null,
            expiring ? PassNotice(left, pass.EntriesTotal ?? left, pass.EndDate, daysLeft) : null,
            null, EntriesLeft: left, EntriesTotal: pass.EntriesTotal);
    }

    /// <summary>"Te quedan 2 entradas de 12 (vence el 30 nov)." (§5.3).</summary>
    public static string PassNotice(int left, int total, DateOnly endsOn, int daysLeft)
    {
        var entries = left == 1 ? "Te queda 1 entrada" : $"Te quedan {left} entradas";
        var when = daysLeft <= 0 ? "vence hoy" : $"vence el {ShortDate(endsOn)}";
        return $"{entries} de {total} ({when}).";
    }

    private static AccessStatus ComputeTime(
        List<MembershipSpan> live, DateOnly today, int graceDays, int expiringDays)
    {
        var started = live.Where(s => s.StartDate <= today).ToList();
        var nextStart = live.Where(s => s.StartDate > today).Select(s => (DateOnly?)s.StartDate).Min();
        if (started.Count == 0)
        {
            // Solo tiene una que empieza más adelante: hoy no está cubierto, pero tampoco "sin plan".
            return Scheduled(nextStart!.Value);
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

        // Pasada la gracia, una membresía que empieza más adelante manda sobre "vencido" (§10).
        if (nextStart is { } next)
        {
            return Scheduled(next);
        }

        return Blocked(
            CommercialStatus.Expired, AccessBlockReason.Expired, end,
            $"Membresía vencida el {ShortDate(end)}, fuera de la gracia.");
    }

    private static AccessStatus Scheduled(DateOnly startsOn)
        => new(
            CommercialStatus.Scheduled, AccessKind.Blocked, AccessBlockReason.Scheduled, null, null, null, null,
            null, $"Programada: empieza el {ShortDate(startsOn)}.", startsOn);

    private static string ExpiringNotice(DateOnly end, int daysLeft)
        => daysLeft <= 0
            ? "Tu plan vence hoy."
            : $"Tu plan vence en {daysLeft} {(daysLeft == 1 ? "día" : "días")} (el {ShortDate(end)}). Renueva en recepción para no perder tu fecha.";

    private static AccessStatus Blocked(
        CommercialStatus commercial, AccessBlockReason reason, DateOnly? endsOn, string message)
        => new(commercial, AccessKind.Blocked, reason, endsOn, null, null, null, null, message);
}
