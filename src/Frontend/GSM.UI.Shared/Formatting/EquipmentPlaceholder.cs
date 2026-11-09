using MudBlazor;

namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Imagen de muestra de una máquina o equipo mientras no haya foto real: una ilustración del tipo (ícono sobre un fondo
/// oscuro con degradado) como <c>data:</c> URI, así funciona igual en la web, en la PWA y en la app nativa sin servir archivos.
/// </summary>
public static class EquipmentPlaceholder
{
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

    private static readonly Dictionary<string, string> Cache = new();

    /// <summary>Imagen del tipo (nombre del enum tal como lo devuelve la API); un tipo desconocido usa las pesas.</summary>
    public static string DataUri(string? typeName)
    {
        var key = typeName is not null && Icons_.ContainsKey(typeName) ? typeName : "Barbell";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var svg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 96 96'>"
            + "<defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'>"
            + "<stop offset='0' stop-color='#2B2B2B'/><stop offset='1' stop-color='#161616'/></linearGradient></defs>"
            + "<rect width='96' height='96' fill='url(#g)'/>"
            + "<g transform='translate(24 24) scale(2)' fill='#B50016'>" + Icons_[key] + "</g></svg>";

        return Cache[key] = "data:image/svg+xml;utf8," + Uri.EscapeDataString(svg);
    }
}
