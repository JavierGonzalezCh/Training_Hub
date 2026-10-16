using GSM.Client.Generated;
using GSM.UI.Shared.Formatting;

namespace GSM.Panel.UI.Onboarding;

/// <summary>
/// Plantillas de plan del onboarding (Gyms §5, paso 5): Día, Mensual, Trimestral y Anual, donde solo se pone el
/// precio. Viven en el cliente: crean planes normales con <c>POST /api/membership-plans</c>.
/// </summary>
public sealed record PlanTemplate(string Key, string Name, int DurationValue, string DurationUnit)
{
    public static readonly IReadOnlyList<PlanTemplate> All =
    [
        new("day", "Día", 1, PlanDurationLabels.Days),
        new("month", "Mensual", 1, PlanDurationLabels.Months),
        new("quarter", "Trimestral", 3, PlanDurationLabels.Months),
        new("year", "Anual", 12, PlanDurationLabels.Months),
    ];

    /// <summary>El plan que crea la plantilla con el precio dado (sin congelamiento: se ajusta luego en Planes).</summary>
    public CreatePlanRequest ToRequest(decimal price) => new()
    {
        Name = Name,
        Price = (double)price,
        DurationValue = DurationValue,
        DurationUnit = DurationUnit,
        Kind = PlanDurationLabels.KindTime,
    };
}
