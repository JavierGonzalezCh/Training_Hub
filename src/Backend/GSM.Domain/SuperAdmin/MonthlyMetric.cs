using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.SuperAdmin;

/// <summary>
/// Foto de las métricas del SaaS de un mes cerrado (<c>GSM-modulo-superadmin.md</c> §V4). Una fila por mes,
/// inmutable: lo que no se guardó no se recupera y lo guardado no se recalcula.
/// </summary>
public sealed class MonthlyMetric : BaseEntity
{
    private MonthlyMetric()
    {
    }

    /// <summary>Primer día del mes que describe la foto.</summary>
    public DateOnly Month { get; private set; }

    /// <summary>Suma de los precios de las suscripciones activas (al día o en mora) al cierre del mes.</summary>
    public decimal Mrr { get; private set; }

    /// <summary>Neto cobrado a los gyms en el mes (pagos del SaaS por día de negocio; los ajustes restan).</summary>
    public decimal Collected { get; private set; }

    public int NewGyms { get; private set; }

    public int TrialGymsAtClose { get; private set; }

    public int ConvertedGyms { get; private set; }

    public int LostGyms { get; private set; }

    public int ActiveGymsAtStart { get; private set; }

    public int ActiveGymsAtClose { get; private set; }

    /// <summary>Gyms perdidos ÷ activos al inicio del mes. <c>null</c> si no había activos al inicio.</summary>
    public decimal? Churn { get; private set; }

    /// <summary>Miembros con una membresía que cubría el último día del mes, en toda la plataforma.</summary>
    public int ActiveMembers { get; private set; }

    /// <summary>Entradas (check-ins) de toda la plataforma en el mes.</summary>
    public int CheckIns { get; private set; }

    /// <summary>
    /// Estado efectivo de cada gym al cierre (gymId → nombre del estado). Es el "estado al inicio" del mes
    /// siguiente: los estados derivados no se persisten y la suspensión manual no deja historial.
    /// </summary>
    public Dictionary<string, string> GymStates { get; private set; } = new();

    public static MonthlyMetric Create(
        DateOnly month,
        decimal mrr,
        decimal collected,
        int newGyms,
        int trialGymsAtClose,
        int convertedGyms,
        int lostGyms,
        int activeGymsAtStart,
        int activeGymsAtClose,
        int activeMembers,
        int checkIns,
        Dictionary<string, string> gymStates)
    {
        if (month.Day != 1)
        {
            throw new ValidationDomainException("El mes de la foto debe ser su primer día.");
        }

        if (new[] { newGyms, trialGymsAtClose, convertedGyms, lostGyms, activeGymsAtStart, activeGymsAtClose, activeMembers, checkIns }
            .Any(v => v < 0))
        {
            throw new ValidationDomainException("Los conteos de la foto no pueden ser negativos.");
        }

        return new MonthlyMetric
        {
            Month = month,
            Mrr = decimal.Round(mrr, 2),
            Collected = decimal.Round(collected, 2),
            NewGyms = newGyms,
            TrialGymsAtClose = trialGymsAtClose,
            ConvertedGyms = convertedGyms,
            LostGyms = lostGyms,
            ActiveGymsAtStart = activeGymsAtStart,
            ActiveGymsAtClose = activeGymsAtClose,
            Churn = activeGymsAtStart == 0 ? null : decimal.Round((decimal)lostGyms / activeGymsAtStart, 4),
            ActiveMembers = activeMembers,
            CheckIns = checkIns,
            GymStates = gymStates,
        };
    }
}
