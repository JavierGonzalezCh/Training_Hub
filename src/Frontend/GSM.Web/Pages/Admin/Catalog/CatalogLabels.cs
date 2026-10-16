using GSM.Client.Generated;
using GSM.UI.Shared.Formatting;

namespace GSM.Web.Pages.Admin.Catalog;

/// <summary>Textos y conversiones de las pantallas de catálogos del sistema (S4). Sin dependencias de Blazor para poder probarlas.</summary>
public static class CatalogLabels
{
    /// <summary>Nombres del enum <c>EquipmentType</c> en el orden de su valor numérico.</summary>
    private static readonly string[] EquipmentTypeNames =
    [
        "Barbell", "Dumbbell", "Kettlebell", "Machine", "Cable", "Smith_machine", "Bench", "Rack",
        "Resistance_band", "Trx", "Medicine_ball", "Mat", "Box", "Jump_rope", "Bodyweight", "Cardio_machine",
    ];

    public static int EquipmentTypeValue(string name)
    {
        var index = Array.IndexOf(EquipmentTypeNames, name);
        return index < 0 ? 0 : index;
    }

    public static string EquipmentTypeLabel(string name) => RoutineLabels.EquipmentLabel(name);

    /// <summary>Solo las alternativas guardadas a mano; las "sugeridas" se calculan al vuelo y no se administran.</summary>
    public static IReadOnlyList<ExerciseAlternativeResponse> RecommendedOnly(IEnumerable<ExerciseAlternativeResponse>? all)
        => (all ?? []).Where(a => a.Source == "recommended").OrderBy(a => a.Name).ToList();

    /// <summary>Texto corto de un ejercicio para la tabla: músculo, tipo y medición.</summary>
    public static string ExerciseSummary(ExerciseResponse e)
        => $"{RoutineLabels.MuscleLabel(e.PrimaryMuscle)} · {RoutineLabels.ExerciseTypeLabel(e.ExerciseType)} · {RoutineLabels.TrackingTypeLabel(e.TrackingType)}";

    public static string StatusLabel(bool isActive) => isActive ? "Activo" : "Inactivo";

    /// <summary>Un modelo "por unidad" es una máquina (una fila por unidad en el gym); si no, un implemento con cantidad.</summary>
    public static string UnitModeLabel(bool trackedPerUnit) => trackedPerUnit ? "Una fila por unidad" : "Con cantidad";

    /// <summary>Tope del servidor para imágenes (<c>ImageUploadRules.MaxBytes</c>).</summary>
    public const long MaxImageBytes = 5 * 1024 * 1024;
}
