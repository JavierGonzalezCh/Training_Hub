using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Memberships;

/// <summary>Dónde está un congelamiento respecto a un día de negocio.</summary>
public enum FreezeState
{
    /// <summary>Todavía no empieza.</summary>
    Scheduled,

    /// <summary>El día cae dentro del congelamiento (o es uno abierto que no se ha reanudado).</summary>
    Active,

    /// <summary>Ya terminó.</summary>
    Ended,
}

/// <summary>
/// Un congelamiento de una membresía (Memberships v2, §5.2). No hay tarea programada: un día dentro
/// de <see cref="StartsOn"/>..<see cref="EndsOn"/> no está cubierto y eso se calcula. Un congelamiento
/// <b>programado</b> (con <see cref="EndsOn"/>) corre de inmediato el fin de la membresía los días que
/// dura; uno <b>abierto</b> (sin fin, solo desde hoy) lo corre al reanudar.
/// </summary>
public sealed class MembershipFreeze : BaseEntity, ITenantEntity
{
    public const int MaxReasonLength = 500;

    private MembershipFreeze()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MembershipId { get; private set; }

    /// <summary>Primer día congelado (inclusive).</summary>
    public DateOnly StartsOn { get; private set; }

    /// <summary>Último día congelado (inclusive). Nulo = abierto: termina al reanudar.</summary>
    public DateOnly? EndsOn { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    /// <summary>El Owner se saltó el límite del plan al crearlo.</summary>
    public bool OverrideLimit { get; private set; }

    /// <summary>
    /// Días que corrieron el fin de la membresía: los de un programado; en uno abierto, 0 hasta que se
    /// reanuda y entonces los días congelados (acotados al límite del plan salvo que se lo haya saltado).
    /// </summary>
    public int DaysCredited { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public bool IsOpen => EndsOn is null;

    public static MembershipFreeze Create(
        Guid gymId, Guid membershipId, DateOnly startsOn, DateOnly? endsOn, string reason,
        bool overrideLimit, int daysCredited, Guid? createdBy)
    {
        if (gymId == Guid.Empty || membershipId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de congelamiento incompletos.");
        }

        if (endsOn is { } end && end < startsOn)
        {
            throw new ValidationDomainException("La fecha de fin no puede ser anterior a la de inicio.");
        }

        return new MembershipFreeze
        {
            GymId = gymId,
            MembershipId = membershipId,
            StartsOn = startsOn,
            EndsOn = endsOn,
            Reason = NormalizeReason(reason),
            OverrideLimit = overrideLimit,
            DaysCredited = Math.Max(0, daysCredited),
            CreatedBy = createdBy,
        };
    }

    public static string NormalizeReason(string? reason)
    {
        var trimmed = (reason ?? string.Empty).Trim();
        return trimmed.Length is < 3 or > MaxReasonLength
            ? throw new ValidationDomainException("El motivo del congelamiento es obligatorio (3 a 500 caracteres).")
            : trimmed;
    }

    /// <summary>¿El día cae dentro del congelamiento? Uno abierto cubre de <see cref="StartsOn"/> en adelante.</summary>
    public bool CoversDate(DateOnly date)
        => StartsOn <= date && (EndsOn is null || date <= EndsOn);

    public FreezeState StateOn(DateOnly today)
        => StartsOn > today ? FreezeState.Scheduled : CoversDate(today) ? FreezeState.Active : FreezeState.Ended;

    /// <summary>Días del tramo (0 si es abierto y aún no se cerró).</summary>
    public int Length => EndsOn is { } end ? end.DayNumber - StartsOn.DayNumber + 1 : 0;

    /// <summary>Días congelados en total que cuentan contra el límite del plan.</summary>
    public static int DaysUsed(IEnumerable<MembershipFreeze> freezes) => freezes.Sum(f => f.DaysCredited);

    /// <summary>Cierra el congelamiento: <paramref name="lastFrozenDay"/> es el último día que quedó congelado.</summary>
    internal void Close(DateOnly lastFrozenDay, int daysCredited)
    {
        EndsOn = lastFrozenDay;
        DaysCredited = Math.Max(0, daysCredited);
    }

    /// <summary>Corre un congelamiento programado cuando la membresía se desplaza (días positivos o negativos).</summary>
    public void Shift(int days)
    {
        StartsOn = StartsOn.AddDays(days);
        if (EndsOn is { } end)
        {
            EndsOn = end.AddDays(days);
        }
    }
}
