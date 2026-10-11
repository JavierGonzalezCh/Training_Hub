using GSM.UI.Shared.Formatting;
using MudBlazor;

namespace GSM.Panel.UI.Pages.Equipment;

/// <summary>
/// Textos e íconos del inventario. El tipo de equipo es el mismo enum de los ejercicios
/// (<see cref="RoutineLabels.Equipment"/>); las imágenes de referencia del catálogo aún no existen, así que
/// cada tipo se reconoce por un ícono.
/// </summary>
internal static class EquipmentDisplay
{
    /// <summary>Estados de una máquina (valor del enum, nombre en la API, etiqueta). E2 les da uso al cambiarlos.</summary>
    public static readonly (int Value, string Name, string Label)[] Statuses =
    [
        (0, "Available", "Disponible"),
        (1, "Maintenance", "En mantenimiento"),
        (2, "OutOfService", "Fuera de servicio"),
    ];

    private static readonly Dictionary<string, string> Icons_ = new()
    {
        ["Barbell"] = Icons.Material.Filled.FitnessCenter,
        ["Dumbbell"] = Icons.Material.Filled.FitnessCenter,
        ["Kettlebell"] = Icons.Material.Filled.SportsGymnastics,
        ["Machine"] = Icons.Material.Filled.PrecisionManufacturing,
        ["Cable"] = Icons.Material.Filled.Cable,
        ["Smith_machine"] = Icons.Material.Filled.ViewColumn,
        ["Bench"] = Icons.Material.Filled.Weekend,
        ["Rack"] = Icons.Material.Filled.Window,
        ["Resistance_band"] = Icons.Material.Filled.Gesture,
        ["Trx"] = Icons.Material.Filled.Link,
        ["Medicine_ball"] = Icons.Material.Filled.SportsBasketball,
        ["Mat"] = Icons.Material.Filled.CropLandscape,
        ["Box"] = Icons.Material.Filled.CheckBoxOutlineBlank,
        ["Jump_rope"] = Icons.Material.Filled.SportsMartialArts,
        ["Bodyweight"] = Icons.Material.Filled.SelfImprovement,
        ["Cardio_machine"] = Icons.Material.Filled.DirectionsRun,
    };

    public static string TypeIcon(string typeName)
        => Icons_.GetValueOrDefault(typeName, Icons.Material.Filled.FitnessCenter);

    /// <summary>Valor numérico del enum (para filtros y cuerpos de request) a partir del nombre que devuelve la API.</summary>
    public static int TypeValue(string typeName)
    {
        var index = Array.FindIndex(TypeNames, n => n == typeName);
        return index < 0 ? 0 : index;
    }

    private static readonly string[] TypeNames =
    [
        "Barbell", "Dumbbell", "Kettlebell", "Machine", "Cable", "Smith_machine", "Bench", "Rack",
        "Resistance_band", "Trx", "Medicine_ball", "Mat", "Box", "Jump_rope", "Bodyweight", "Cardio_machine",
    ];

    public static string TypeLabel(string typeName) => RoutineLabels.EquipmentLabel(typeName);

    public static string StatusLabel(string statusName)
        => Statuses.FirstOrDefault(s => s.Name == statusName).Label ?? statusName;

    public static Color StatusColor(string statusName) => statusName switch
    {
        "Available" => Color.Success,
        "Maintenance" => Color.Warning,
        "OutOfService" => Color.Error,
        _ => Color.Default,
    };
}
