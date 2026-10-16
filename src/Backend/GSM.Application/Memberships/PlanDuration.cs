using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using GSM.Domain.Memberships;

namespace GSM.Application.Memberships;

/// <summary>Lectura de la unidad de duración que llega en el texto de la API (<c>days</c> / <c>months</c>).</summary>
public static class PlanDuration
{
    /// <summary>Texto aceptado como unidad; vacío = días (compatibilidad con los planes de antes).</summary>
    public static bool IsKnown(string? text)
        => EnumText.TryParse<DurationUnit>(text, out _);

    /// <summary>Tipo de plan que llega en el texto de la API (<c>time</c> / <c>pass</c>); vacío = por tiempo.</summary>
    public static bool IsKnownKind(string? text)
        => EnumText.TryParse<PlanKind>(text, out _);

    public static PlanKind ParseKind(string? text)
    {
        if (!EnumText.TryParse<PlanKind>(text, out PlanKind? kind))
        {
            throw new ValidationDomainException("El tipo de plan debe ser 'time' o 'pass'.");
        }

        return kind ?? PlanKind.Time;
    }

    /// <summary>
    /// Duración efectiva del plan: en una tiquetera es su vigencia en días (<paramref name="validityDays"/>);
    /// en un plan por tiempo, la duración pedida.
    /// </summary>
    public static int EffectiveDuration(PlanKind kind, int durationValue, int? validityDays)
        => kind == PlanKind.Pass
            ? validityDays ?? throw new ValidationDomainException("Una tiquetera necesita sus días de vigencia.")
            : durationValue;

    public static DurationUnit ParseUnit(string? text)
    {
        if (!EnumText.TryParse<DurationUnit>(text, out DurationUnit? unit))
        {
            throw new ValidationDomainException("La unidad de duración debe ser 'days' o 'months'.");
        }

        return unit ?? DurationUnit.Days;
    }
}
