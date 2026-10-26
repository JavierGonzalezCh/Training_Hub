using GSM.Client.Generated;
using GSM.UI.Shared.Formatting;

namespace GSM.Panel.UI.Onboarding;

/// <summary>
/// Plantillas de plan del onboarding (Gyms §5, paso 5): Día, Mensual, Trimestral y Anual, donde solo se pone el
/// precio. Viven en el cliente: crean planes normales con <c>POST /api/membership-plans</c>.
/// </summary>
public sealed record PlanTemplate(
    string Key, string Name, int DurationValue, string DurationUnit, string Kind = PlanDurationLabels.KindTime, int? Entries = null)
{
    public static readonly IReadOnlyList<PlanTemplate> All =
    [
        new("day", "Día", 1, PlanDurationLabels.Days),
        new("month", "Mensual", 1, PlanDurationLabels.Months),
        new("quarter", "Trimestral", 3, PlanDurationLabels.Months),
        new("year", "Anual", 12, PlanDurationLabels.Months),
    ];

    /// <summary>
    /// Los planes que el onboarding sugiere según el tipo de negocio (docs/GSM-tipos-de-negocio.md §8). Son solo
    /// sugerencias: el dueño las elige y pone el precio, que nunca viene relleno. Gimnasio y «otro» conservan las cuatro
    /// plantillas de siempre; el tipo desconocido cae ahí también.
    /// </summary>
    public static IReadOnlyList<PlanTemplate> For(string? gymType) => gymType switch
    {
        "Box" => [Monthly, new("pass12", "Tiquetera 12 clases", 45, PlanDurationLabels.Days, PlanDurationLabels.KindPass, 12)],
        "Studio" =>
        [
            new("pass8", "Tiquetera 8 clases", 30, PlanDurationLabels.Days, PlanDurationLabels.KindPass, 8),
            new("pass4", "Tiquetera 4 clases", 30, PlanDurationLabels.Days, PlanDurationLabels.KindPass, 4),
            new("single", "Clase suelta", 7, PlanDurationLabels.Days, PlanDurationLabels.KindPass, 1),
        ],
        "PersonalTrainer" =>
        [
            new("month30", "Mensual", 30, PlanDurationLabels.Days),
            new("pass12", "Paquete 12 sesiones", 45, PlanDurationLabels.Days, PlanDurationLabels.KindPass, 12),
        ],
        "Academy" => [Monthly, Quarterly],
        _ => All,
    };

    private static PlanTemplate Monthly => All[1];

    private static PlanTemplate Quarterly => All[2];

    /// <summary>El plan que crea la plantilla con el precio dado (sin congelamiento: se ajusta luego en Planes).</summary>
    public CreatePlanRequest ToRequest(decimal price) => new()
    {
        Name = Name,
        Price = (double)price,
        DurationValue = DurationValue,
        DurationUnit = DurationUnit,
        Kind = Kind,
        Entries = Entries,
        ValidityDays = Entries is null ? null : DurationValue,
    };
}
