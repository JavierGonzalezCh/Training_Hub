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

    /// <summary>
    /// En almacenamiento solo <see cref="MembershipStatus.Active"/> o <see cref="MembershipStatus.Cancelled"/>:
    /// <c>expired</c> se deriva de la fecha y, desde MS4, <c>paused</c> se calcula con los congelamientos
    /// (<see cref="MembershipFreeze"/>).
    /// </summary>
    public MembershipStatus Status { get; private set; } = MembershipStatus.Active;

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

    /// <summary>
    /// Entradas que trae (MS5). Solo tiqueteras; nulo en una membresía por tiempo. Se copia del plan al asignar y
    /// no cambia aunque el plan cambie. Las usadas se calculan con <see cref="PassUsage"/> (§7).
    /// </summary>
    public int? EntriesTotal { get; private set; }

    public bool IsPass => EntriesTotal is not null;

    /// <summary>Total que se cobra al asignarla: precio acordado + matrícula (el crédito de un cambio de plan llega en MS6).</summary>
    public decimal TotalToCharge => AgreedPrice + EnrollmentFee;

    public static Membership Create(
        Guid gymId, Guid memberId, Guid planId, DateOnly startDate, int durationValue,
        RenewalKind renewalKind = RenewalKind.New, string? startOverrideReason = null,
        MembershipPricing? pricing = null, DurationUnit durationUnit = DurationUnit.Days,
        int? inheritedCycleDay = null, int? entriesTotal = null)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || planId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de membresía incompletos.");
        }

        if (durationValue < 1)
        {
            throw new ValidationDomainException("La duración debe ser de al menos 1.");
        }

        if (entriesTotal is < 1)
        {
            throw new ValidationDomainException("Una tiquetera necesita al menos una entrada.");
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
            EntriesTotal = entriesTotal,
            Status = MembershipStatus.Active,
            RenewalKind = renewalKind,
            StartOverrideReason = string.IsNullOrWhiteSpace(startOverrideReason) ? null : startOverrideReason.Trim(),
            AgreedPrice = pricing?.AgreedPrice ?? 0m,
            DiscountReason = pricing?.DiscountReason,
            EnrollmentFee = pricing?.EnrollmentFee ?? 0m,
            EnrollmentWaivedReason = pricing?.EnrollmentWaivedReason,
        };
    }

    /// <summary>
    /// Estado efectivo en una fecha: materializa <see cref="MembershipStatus.Expired"/> y, desde MS4,
    /// <see cref="MembershipStatus.Paused"/> (el día cae dentro de un congelamiento: se calcula, no se guarda).
    /// </summary>
    public MembershipStatus EffectiveStatusOn(DateOnly today, bool frozenToday = false)
        => Status is MembershipStatus.Paused or MembershipStatus.Cancelled
            ? Status
            : frozenToday
                ? MembershipStatus.Paused
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
        CancelledAt = now;
        CancellationReason = trimmed;
    }

    /// <summary>
    /// Crea un congelamiento (§5.2). Sin <paramref name="startsOn"/> ni <paramref name="endsOn"/> es uno
    /// abierto desde hoy: el fin se corre al reanudar. Con <paramref name="endsOn"/> es programado: el fin
    /// de esta membresía se corre ya los días que dura y el llamador corre las encadenadas con
    /// <c>DaysAdded</c>. Se valida contra el plan salvo que <paramref name="overrideLimit"/> (solo el Owner).
    /// </summary>
    public (MembershipFreeze Freeze, int DaysAdded) Freeze(
        MembershipPlan plan, IReadOnlyCollection<MembershipFreeze> existing, DateOnly today,
        DateOnly? startsOn, DateOnly? endsOn, string? reason, bool overrideLimit, Guid? createdBy)
    {
        if (Status != MembershipStatus.Active)
        {
            throw new ConflictException("Solo se puede congelar una membresía activa.");
        }

        var cleanReason = MembershipFreeze.NormalizeReason(reason);
        var start = startsOn ?? today;
        if (start < today)
        {
            throw new ValidationDomainException("Un congelamiento no puede empezar en el pasado.");
        }

        if (endsOn is null && start != today)
        {
            throw new ValidationDomainException("Un congelamiento sin fecha de fin solo puede empezar hoy.");
        }

        if (endsOn is { } end && end < start)
        {
            throw new ValidationDomainException("La fecha de fin no puede ser anterior a la de inicio.");
        }

        if (existing.Any(f => (f.EndsOn is null || f.EndsOn >= start) && (endsOn is null || f.StartsOn <= endsOn)))
        {
            throw new ConflictException("La membresía ya tiene un congelamiento en esas fechas.");
        }

        if (endsOn is null)
        {
            if (!CoversDate(today))
            {
                throw new ValidationDomainException("Solo se puede congelar la membresía que está vigente hoy.");
            }
        }
        else if (start < StartDate || start > EndDate)
        {
            throw new ValidationDomainException(
                $"El congelamiento debe empezar mientras la membresía está vigente ({StartDate:yyyy-MM-dd} a {EndDate:yyyy-MM-dd}).");
        }

        var days = endsOn is { } last ? last.DayNumber - start.DayNumber + 1 : (int?)null;
        var violation = FindLimitViolation(plan, MembershipFreeze.DaysUsed(existing), days);
        if (violation is not null && !overrideLimit)
        {
            throw new ValidationDomainException(violation);
        }

        var credited = days ?? 0;
        var freeze = MembershipFreeze.Create(
            GymId, Id, start, endsOn, cleanReason, violation is not null, credited, createdBy);
        EndDate = EndDate.AddDays(credited);
        return (freeze, credited);
    }

    private static string? FindLimitViolation(MembershipPlan plan, int used, int? days)
    {
        if (plan.MaxFreezeDays == 0)
        {
            return "Este plan no permite congelar.";
        }

        var usedText = $"Este plan permite congelar hasta {plan.MaxFreezeDays} días en total y ya se usaron {used}.";
        if (days is { } d)
        {
            if (d < plan.MinFreezeDays)
            {
                return $"El congelamiento mínimo de este plan es de {plan.MinFreezeDays} días.";
            }

            return used + d > plan.MaxFreezeDays ? usedText : null;
        }

        // Abierto: al menos tiene que caber un congelamiento mínimo.
        return plan.MaxFreezeDays - used < plan.MinFreezeDays ? usedText : null;
    }

    /// <summary>
    /// Termina el congelamiento que cubre hoy (reanudar, también antes de tiempo). Uno abierto corre el
    /// fin los días que estuvo congelada; uno programado en curso devuelve los días que ya no se usan. Si
    /// aún no pasó ningún día congelado, el congelamiento se elimina. <see cref="FreezeEnd.Delta"/> es lo
    /// que hay que correr las membresías encadenadas detrás (negativo si se acorta).
    /// </summary>
    public FreezeEnd EndFreeze(MembershipPlan plan, IReadOnlyCollection<MembershipFreeze> freezes, DateOnly today)
    {
        var current = freezes.FirstOrDefault(f => f.CoversDate(today))
            ?? throw new ConflictException("La membresía no está congelada.");
        var lastFrozen = today.AddDays(-1);

        if (current.StartsOn > lastFrozen)
        {
            // Empezaba hoy: no llegó a congelarse ningún día.
            var undo = -current.DaysCredited;
            EndDate = EndDate.AddDays(undo);
            return new FreezeEnd(current, undo, Removed: true);
        }

        if (current.EndsOn is null)
        {
            var actual = today.DayNumber - current.StartsOn.DayNumber;
            var usedByOthers = freezes.Where(f => f.Id != current.Id).Sum(f => f.DaysCredited);
            var credited = current.OverrideLimit
                ? actual
                : Math.Min(actual, Math.Max(0, plan.MaxFreezeDays - usedByOthers));
            current.Close(lastFrozen, credited);
            EndDate = EndDate.AddDays(credited);
            return new FreezeEnd(current, credited, Removed: false);
        }

        var returned = current.EndsOn.Value.DayNumber - lastFrozen.DayNumber;
        current.Close(lastFrozen, current.DaysCredited - returned);
        EndDate = EndDate.AddDays(-returned);
        return new FreezeEnd(current, -returned, Removed: false);
    }

    /// <summary>Deshace un congelamiento programado que aún no empieza: devuelve su fin. Devuelve el desplazamiento (negativo).</summary>
    public int RemoveScheduledFreeze(MembershipFreeze freeze, DateOnly today)
    {
        if (freeze.MembershipId != Id)
        {
            throw new NotFoundException("Congelamiento no encontrado.");
        }

        if (freeze.StartsOn <= today)
        {
            throw new ConflictException("El congelamiento ya empezó: reanuda la membresía para terminarlo.");
        }

        var undo = -freeze.DaysCredited;
        EndDate = EndDate.AddDays(undo);
        return undo;
    }

    /// <summary>
    /// Corre una membresía futura (encadenada) cuando la anterior se extiende o se acorta por un
    /// congelamiento (días positivos o negativos). No toca <see cref="CycleDay"/>: la renovación que
    /// sigue arranca ciclo nuevo si no coincide (<see cref="MembershipCalendar.ResolveCycleDay"/>).
    /// </summary>
    public void ShiftBy(int days)
    {
        StartDate = StartDate.AddDays(days);
        EndDate = EndDate.AddDays(days);
    }

    /// <summary>
    /// Días de negocio realmente cubiertos hasta <paramref name="today"/> inclusive (antigüedad, L2): cuenta
    /// desde el inicio sin pasar de hoy ni del fin, descuenta los días congelados (MS4: tramos de <see cref="MembershipFreeze"/>) y no cuenta nada si está
    /// cancelada. Una congelada hoy deja de sumar desde el día que se congeló. Lo no vivido aún (un
    /// periodo pagado por adelantado) no cuenta.
    /// </summary>
    public int CoveredDaysThrough(DateOnly today, IReadOnlyCollection<MembershipFreeze> freezes)
    {
        if (Status == MembershipStatus.Cancelled)
        {
            return 0;
        }

        var last = EndDate < today ? EndDate : today;
        var own = freezes.Where(f => f.MembershipId == Id).ToList();
        var days = 0;
        for (var day = StartDate; day <= last; day = day.AddDays(1))
        {
            if (!own.Any(f => f.CoversDate(day)))
            {
                days++;
            }
        }

        return days;
    }

    public bool CoversDate(DateOnly date)
        => Status == MembershipStatus.Active && StartDate <= date && date <= EndDate;
}

/// <summary>Resultado de terminar un congelamiento: cuánto correr las encadenadas y si la fila se elimina.</summary>
public sealed record FreezeEnd(MembershipFreeze Freeze, int Delta, bool Removed);
