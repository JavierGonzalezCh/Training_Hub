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

    public static DurationUnit ParseUnit(string? text)
    {
        if (!EnumText.TryParse<DurationUnit>(text, out DurationUnit? unit))
        {
            throw new ValidationDomainException("La unidad de duración debe ser 'days' o 'months'.");
        }

        return unit ?? DurationUnit.Days;
    }
}
