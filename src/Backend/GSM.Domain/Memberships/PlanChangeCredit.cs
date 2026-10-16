using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Memberships;

/// <summary>
/// Crédito de lo no usado de una membresía vigente al cambiar de plan (Memberships v2 §8).
/// Por tiempo: base × días que quedan ÷ días totales. Tiquetera: base × entradas que quedan ÷ entradas totales.
/// La <see cref="Base"/> es lo que de verdad se pagó por el plan (sin matrícula, que no se devuelve).
/// </summary>
public sealed record PlanChangeCredit(
    decimal NetPaid,
    decimal Base,
    decimal Calculated,
    int? DaysLeft,
    int? DaysTotal,
    int? EntriesLeft,
    int? EntriesTotal)
{
    public bool IsPass => EntriesTotal is not null;

    /// <summary>
    /// Calcula el crédito de <paramref name="membership"/> hoy. <paramref name="netPaid"/> es la suma de los pagos
    /// ligados a ella (las anulaciones ya restan). La base cuenta también el crédito que la propia membresía trajo de un
    /// cambio anterior, porque ese dinero ya estaba pagado: <c>min(precio acordado, pagado + crédito aplicado)</c>.
    /// Los congelamientos no consumen días: uno programado se descuenta de lo que queda y uno abierto, que aún no
    /// corrió el fin, deja el saldo contado desde el día en que empezó.
    /// </summary>
    public static PlanChangeCredit Compute(
        Membership membership, IReadOnlyCollection<MembershipFreeze> freezes, int entriesUsed,
        decimal netPaid, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(membership);

        if (!membership.CoversDate(today))
        {
            throw new ConflictException("Solo se puede cambiar el plan de la membresía que está vigente hoy.");
        }

        var baseAmount = Math.Max(0m, Math.Min(membership.AgreedPrice, netPaid + membership.CreditApplied));

        if (membership.EntriesTotal is { } total)
        {
            var left = Math.Clamp(total - entriesUsed, 0, total);
            return new PlanChangeCredit(
                netPaid, baseAmount, Prorate(baseAmount, left, total), null, null, left, total);
        }

        var own = freezes.Where(f => f.MembershipId == membership.Id).ToList();
        var bounded = own.Where(f => f.EndsOn is not null).ToList();
        var totalDays = Math.Max(
            1, membership.EndDate.DayNumber - membership.StartDate.DayNumber + 1 - bounded.Sum(f => f.DaysCredited));

        var from = own.FirstOrDefault(f => f.IsOpen && f.CoversDate(today))?.StartsOn ?? today;
        var daysLeft = membership.EndDate.DayNumber - from.DayNumber + 1;
        foreach (var freeze in bounded)
        {
            var first = freeze.StartsOn > from ? freeze.StartsOn : from;
            var last = freeze.EndsOn!.Value < membership.EndDate ? freeze.EndsOn!.Value : membership.EndDate;
            if (last >= first)
            {
                daysLeft -= last.DayNumber - first.DayNumber + 1;
            }
        }

        daysLeft = Math.Clamp(daysLeft, 0, totalDays);
        return new PlanChangeCredit(
            netPaid, baseAmount, Prorate(baseAmount, daysLeft, totalDays), daysLeft, totalDays, null, null);
    }

    private static decimal Prorate(decimal amount, int left, int total)
        => total <= 0 ? 0m : decimal.Round(amount * left / total, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Crédito que finalmente se aplica: el calculado o el que el staff ajustó (con motivo de 3 a 500 caracteres,
    /// entre 0 y la base pagada). Devuelve también el motivo normalizado (nulo si no se ajustó).
    /// </summary>
    public (decimal Credit, string? Reason) Resolve(decimal? requested, string? reason)
    {
        if (requested is null || decimal.Round(requested.Value, 2) == Calculated)
        {
            return (Calculated, null);
        }

        var credit = decimal.Round(requested.Value, 2);
        if (credit < 0 || credit > Base)
        {
            throw new ValidationDomainException(
                $"El crédito ajustado debe estar entre 0 y lo pagado por el plan ({Base:0.##}).");
        }

        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is < 3 or > 500)
        {
            throw new ValidationDomainException("El motivo del ajuste del crédito es obligatorio (3 a 500 caracteres).");
        }

        return (credit, trimmed);
    }
}
