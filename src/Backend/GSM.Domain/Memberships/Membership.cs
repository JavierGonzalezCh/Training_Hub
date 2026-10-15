using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Memberships;

/// <summary>
/// Vigencia de un miembro. <see cref="EndDate"/> es el último día cubierto (inclusive).
/// Una renovación es otra <see cref="Membership"/> que arranca al día siguiente de la anterior,
/// de modo que nunca hay dos que cubran la misma fecha.
/// </summary>
public sealed class Membership : BaseEntity, ITenantEntity
{
    private Membership()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public Guid PlanId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    /// <summary>En almacenamiento nunca <see cref="MembershipStatus.Expired"/>: ese se deriva de la fecha.</summary>
    public MembershipStatus Status { get; private set; } = MembershipStatus.Active;

    /// <summary>Día de negocio en que se congeló. Solo con <see cref="MembershipStatus.Paused"/>.</summary>
    public DateOnly? PausedOn { get; private set; }

    /// <summary>Días que estuvo congelada en pausas ya cerradas (L2: la antigüedad no cuenta los días congelados).</summary>
    public int PausedDays { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Cómo se calculó el inicio (§4.1 de Memberships v2).</summary>
    public RenewalKind RenewalKind { get; private set; } = RenewalKind.New;

    /// <summary>Motivo cuando el staff cambió a mano la fecha de inicio que calculó el sistema.</summary>
    public string? StartOverrideReason { get; private set; }

    /// <summary>Precio acordado para esta membresía (el del plan, o menos con descuento). No cambia si el plan cambia de precio.</summary>
    public decimal AgreedPrice { get; private set; }

    /// <summary>Motivo del descuento. Obligatorio si <see cref="AgreedPrice"/> es menor que el precio del plan.</summary>
    public string? DiscountReason { get; private set; }

    /// <summary>Matrícula cobrada con esta membresía (0 si no aplica o se exoneró).</summary>
    public decimal EnrollmentFee { get; private set; }

    /// <summary>Por qué se exoneró la matrícula. Solo si aplicaba y no se cobró.</summary>
    public string? EnrollmentWaivedReason { get; private set; }

    /// <summary>Día del mes en que arranca el ciclo (1–31). Solo en planes en meses del calendario; nulo en planes por días.</summary>
    public int? CycleDay { get; private set; }

    /// <summary>Total que se cobra al asignarla: precio acordado + matrícula (el crédito de un cambio de plan llega en MS6).</summary>
    public decimal TotalToCharge => AgreedPrice + EnrollmentFee;

    public static Membership Create(
        Guid gymId, Guid memberId, Guid planId, DateOnly startDate, int durationValue,
        RenewalKind renewalKind = RenewalKind.New, string? startOverrideReason = null,
        MembershipPricing? pricing = null, DurationUnit durationUnit = DurationUnit.Days,
        int? inheritedCycleDay = null)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || planId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de membresía incompletos.");
        }

        if (durationValue < 1)
        {
            throw new ValidationDomainException("La duración debe ser de al menos 1.");
        }

        var (endDate, cycleDay) = MembershipCalendar.Resolve(startDate, durationUnit, durationValue, inheritedCycleDay);

        return new Membership
        {
            GymId = gymId,
            MemberId = memberId,
            PlanId = planId,
            StartDate = startDate,
            EndDate = endDate,
            CycleDay = cycleDay,
            Status = MembershipStatus.Active,
            RenewalKind = renewalKind,
            StartOverrideReason = string.IsNullOrWhiteSpace(startOverrideReason) ? null : startOverrideReason.Trim(),
            AgreedPrice = pricing?.AgreedPrice ?? 0m,
            DiscountReason = pricing?.DiscountReason,
            EnrollmentFee = pricing?.EnrollmentFee ?? 0m,
            EnrollmentWaivedReason = pricing?.EnrollmentWaivedReason,
        };
    }

    /// <summary>Estado efectivo en una fecha: materializa <see cref="MembershipStatus.Expired"/>.</summary>
    public MembershipStatus EffectiveStatusOn(DateOnly today)
        => Status is MembershipStatus.Paused or MembershipStatus.Cancelled
            ? Status
            : EndDate < today
                ? MembershipStatus.Expired
                : MembershipStatus.Active;

    /// <summary>
    /// Anula una membresía asignada por error. No borra la fila (queda en el historial) y deja de
    /// cubrir fechas. Los pagos enlazados no se tocan: se anulan aparte, desde Payments.
    /// </summary>
    public void Cancel(string reason, DateTimeOffset now)
    {
        if (Status == MembershipStatus.Cancelled)
        {
            throw new ConflictException("La membresía ya está cancelada.");
        }

        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is < 3 or > 500)
        {
            throw new ValidationDomainException("El motivo de la cancelación es obligatorio (3 a 500 caracteres).");
        }

        Status = MembershipStatus.Cancelled;
        PausedOn = null;
        CancelledAt = now;
        CancellationReason = trimmed;
    }

    /// <summary>Congela la membresía que cubre hoy. Mientras está congelada no da acceso.</summary>
    public void Pause(DateOnly today)
    {
        if (Status != MembershipStatus.Active)
        {
            throw new ConflictException("Solo se puede congelar una membresía activa.");
        }

        if (!CoversDate(today))
        {
            throw new ValidationDomainException("Solo se puede congelar la membresía que está vigente hoy.");
        }

        Status = MembershipStatus.Paused;
        PausedOn = today;
    }

    /// <summary>
    /// Reanuda una membresía congelada y corre su fin tantos días como estuvo congelada. Devuelve
    /// esos días para que el caso de uso corra también las membresías encadenadas detrás.
    /// </summary>
    public int Resume(DateOnly today)
    {
        if (Status != MembershipStatus.Paused || PausedOn is not { } pausedOn)
        {
            throw new ConflictException("La membresía no está congelada.");
        }

        var days = Math.Max(0, today.DayNumber - pausedOn.DayNumber);
        EndDate = EndDate.AddDays(days);
        PausedDays += days;
        Status = MembershipStatus.Active;
        PausedOn = null;
        return days;
    }

    /// <summary>Corre una membresía futura (encadenada) cuando la anterior se extiende al reanudarse.</summary>
    public void ShiftBy(int days)
    {
        if (days < 0)
        {
            throw new ValidationDomainException("Solo se puede correr una membresía hacia adelante.");
        }

        StartDate = StartDate.AddDays(days);
        EndDate = EndDate.AddDays(days);
    }

    /// <summary>
    /// Días de negocio realmente cubiertos hasta <paramref name="today"/> inclusive (antigüedad, L2): cuenta
    /// desde el inicio sin pasar de hoy ni del fin, descuenta los días congelados y no cuenta nada si está
    /// cancelada. Una congelada hoy deja de sumar desde el día que se congeló. Lo no vivido aún (un
    /// periodo pagado por adelantado) no cuenta.
    /// </summary>
    public int CoveredDaysThrough(DateOnly today)
    {
        if (Status == MembershipStatus.Cancelled)
        {
            return 0;
        }

        var last = Status == MembershipStatus.Paused && PausedOn is { } pausedOn ? pausedOn.AddDays(-1) : EndDate;
        if (last > today)
        {
            last = today;
        }

        var span = last.DayNumber - StartDate.DayNumber + 1;
        return span <= 0 ? 0 : Math.Max(0, span - PausedDays);
    }

    public bool CoversDate(DateOnly date)
        => Status == MembershipStatus.Active && StartDate <= date && date <= EndDate;
}
