using GSM.Application.Common.Abstractions;
using GSM.Infrastructure.Common;

namespace GSM.IntegrationTests;

/// <summary>Reloj de tests: por defecto usa el reloj real de Bogotá; se puede fijar a un día de negocio.</summary>
public sealed class MutableClock(SystemClock inner) : IClock
{
    private (DateTimeOffset Utc, DateOnly Day)? _pinned;

    /// <summary>Fija el reloj al mediodía (hora del gym) del día indicado.</summary>
    public void PinToBusinessDay(DateOnly day)
        => _pinned = (inner.BusinessDayRange(day).Start.AddHours(12), day);

    /// <summary>
    /// Fija un instante exacto y su día de negocio. Para los casos límite en que ambos no
    /// coinciden en UTC: de noche en Colombia, el instante ya cae en la fecha UTC siguiente.
    /// </summary>
    public void PinTo(DateTimeOffset utcNow, DateOnly businessDay) => _pinned = (utcNow, businessDay);

    public void Unpin() => _pinned = null;

    public DateTimeOffset UtcNow => _pinned?.Utc ?? inner.UtcNow;

    public DateOnly Today => _pinned?.Day ?? inner.Today;

    public (DateTimeOffset Start, DateTimeOffset End) BusinessDayRange(DateOnly businessDay)
        => inner.BusinessDayRange(businessDay);

    public DateTime ToBusinessLocal(DateTimeOffset instant) => inner.ToBusinessLocal(instant);
}
