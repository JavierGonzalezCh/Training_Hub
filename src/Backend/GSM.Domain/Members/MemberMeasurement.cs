using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Members;

/// <summary>
/// Una medición de peso (y, opcionalmente, altura) de un miembro (Members v2, §3.2). Solo se añade o se
/// borra: no se edita. El IMC se calcula, no se guarda. Son datos de salud: el acceso lo gobierna el
/// permiso <c>ViewMemberHealth</c> y la autorización de datos del miembro.
/// </summary>
public sealed class MemberMeasurement : BaseEntity, ITenantEntity
{
    public const decimal MinWeightKg = 20m;
    public const decimal MaxWeightKg = 400m;
    public const decimal MinHeightCm = 50m;
    public const decimal MaxHeightCm = 250m;

    private MemberMeasurement()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    /// <summary>Día de negocio en que se tomó la medida (no puede ser futuro).</summary>
    public DateOnly MeasuredOn { get; private set; }

    /// <summary>Kilogramos, 20–400, a dos decimales.</summary>
    public decimal WeightKg { get; private set; }

    /// <summary>Centímetros, 50–250, a un decimal. Nulo: vale la última altura registrada.</summary>
    public decimal? HeightCm { get; private set; }

    public Guid? RecordedByUserId { get; private set; }

    /// <summary><c>member</c>, <c>staff</c>, <c>owner</c> o <c>superadmin</c> (texto simple, sin enum de Postgres).</summary>
    public string RecordedByRole { get; private set; } = string.Empty;

    /// <param name="today">Hoy en la zona del gym (<c>IClock.Today</c>).</param>
    public static MemberMeasurement Add(
        Guid gymId,
        Guid memberId,
        DateOnly measuredOn,
        DateOnly today,
        decimal weightKg,
        decimal? heightCm,
        Guid? recordedByUserId,
        string recordedByRole)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || string.IsNullOrWhiteSpace(recordedByRole))
        {
            throw new ValidationDomainException("Medición inválida.");
        }

        if (measuredOn > today)
        {
            throw new ValidationDomainException("La fecha de la medición no puede ser futura.");
        }

        var weight = Math.Round(weightKg, 2, MidpointRounding.AwayFromZero);
        if (weight < MinWeightKg || weight > MaxWeightKg)
        {
            throw new ValidationDomainException($"El peso debe estar entre {MinWeightKg:0} y {MaxWeightKg:0} kg.");
        }

        decimal? height = null;
        if (heightCm is { } h)
        {
            height = Math.Round(h, 1, MidpointRounding.AwayFromZero);
            if (height < MinHeightCm || height > MaxHeightCm)
            {
                throw new ValidationDomainException($"La altura debe estar entre {MinHeightCm:0} y {MaxHeightCm:0} cm.");
            }
        }

        return new MemberMeasurement
        {
            GymId = gymId,
            MemberId = memberId,
            MeasuredOn = measuredOn,
            WeightKg = weight,
            HeightCm = height,
            RecordedByUserId = recordedByUserId,
            RecordedByRole = recordedByRole,
        };
    }

    /// <summary>Resumen de la fila para el historial de cambios cuando se borra.</summary>
    public string Summary()
        => FormattableString.Invariant(
            $"{MeasuredOn:yyyy-MM-dd}: {WeightKg:0.##} kg{(HeightCm is { } h ? $", {h:0.#} cm" : string.Empty)}");

    /// <summary>Índice de masa corporal (kg/m²) a un decimal; nulo si falta la altura.</summary>
    public static decimal? Bmi(decimal weightKg, decimal? heightCm)
    {
        if (heightCm is not { } h || h <= 0)
        {
            return null;
        }

        var meters = h / 100m;
        return Math.Round(weightKg / (meters * meters), 1, MidpointRounding.AwayFromZero);
    }
}
