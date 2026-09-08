using GSM.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Common;

/// <summary>Zona horaria de negocio. Un solo país en el MVP; si se opera en varios, pasa a ser por gym.</summary>
public sealed class ClockOptions
{
    public const string SectionName = "Clock";

    /// <summary>Id IANA de la zona (.NET los resuelve también en Windows).</summary>
    public string TimeZone { get; init; } = "America/Bogota";
}

/// <inheritdoc cref="IClock" />
public sealed class SystemClock : IClock
{
    private readonly TimeZoneInfo _timeZone;

    public SystemClock(IOptions<ClockOptions> options)
    {
        var id = options.Value.TimeZone;

        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Fallar al arrancar es preferible a calcular fechas de negocio en la zona equivocada.
            throw new InvalidOperationException(
                $"La zona horaria '{id}' de 'Clock:TimeZone' no existe en este sistema.", ex);
        }
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, _timeZone).DateTime);

    public (DateTimeOffset Start, DateTimeOffset End) BusinessDayRange(DateOnly businessDay)
    {
        var localMidnight = businessDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = _timeZone.GetUtcOffset(localMidnight);
        var start = new DateTimeOffset(localMidnight, offset).ToUniversalTime();
        return (start, start.AddDays(1));
    }

    public DateTime ToBusinessLocal(DateTimeOffset instant)
        => TimeZoneInfo.ConvertTime(instant, _timeZone).DateTime;
}
