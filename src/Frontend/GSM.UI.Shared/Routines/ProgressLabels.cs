using System.Globalization;
using GSM.Client.Generated;

namespace GSM.UI.Shared.Routines;

/// <summary>La medida que se grafica de un ejercicio.</summary>
public sealed record PrimaryMetric(string Label, string Unit, Func<ProgressPoint, double?> Pick);

/// <summary>Textos y series del progreso por ejercicio (R7), compartidos por el panel y la app del miembro.</summary>
public static class ProgressLabels
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-CO");

    public static string RecordLabel(string kind) => kind switch
    {
        "best_weight" => "Mejor peso",
        "best_set_volume" => "Mejor serie (peso × repeticiones)",
        "best_session_volume" => "Mejor volumen de un día",
        "best_reps" => "Más repeticiones en una serie",
        "best_session_reps" => "Más repeticiones en un día",
        "best_duration" => "Mayor tiempo",
        "best_distance" => "Mayor distancia",
        _ => kind,
    };

    /// <summary>Valor de un récord con su unidad: <c>80 kg</c>, <c>12 reps</c>, <c>1:30</c> (tiempo), <c>400 m</c>.</summary>
    public static string FormatValue(double value, string? unit) => unit switch
    {
        "kg" => $"{value.ToString("0.##", Es)} kg",
        "reps" => $"{value.ToString("0", Es)} reps",
        "s" => FormatSeconds((int)Math.Round(value)),
        "m" => $"{value.ToString("0", Es)} m",
        _ => value.ToString("0.##", Es),
    };

    public static string FormatSeconds(int seconds)
        => seconds >= 3600
            ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>La medida que se grafica según lo que mide el ejercicio: su nombre, su unidad y cómo sacarla de un punto.</summary>
    public static PrimaryMetric Primary(string? tracking) => tracking switch
    {
        "Reps" => new("Repeticiones por día", "reps", p => p.TotalReps),
        "Time" => new("Mejor tiempo", "s", p => p.BestDurationSeconds),
        "Distance" => new("Mejor distancia", "m", p => p.BestDistanceM),
        "TimeDistance" => new("Mejor distancia", "m", p => p.BestDistanceM),
        "WeightDistance" => new("Mejor peso", "kg", p => p.BestWeightKg),
        _ => new("Mejor peso", "kg", p => p.BestWeightKg),
    };

    /// <summary>La serie a graficar (fecha, valor) de un ejercicio, del más antiguo al más reciente; sin los días sin esa medida.</summary>
    public static IReadOnlyList<(DateOnly Day, double Value)> Series(ExerciseProgressResponse progress)
    {
        var pick = Primary(progress.TrackingType).Pick;
        return (progress.Points ?? [])
            .OrderBy(p => p.PerformedOn)
            .Select(p => (p.PerformedOn, Value: pick(p)))
            .Where(x => x.Value is not null)
            .Select(x => (x.PerformedOn, x.Value!.Value))
            .ToList();
    }

    /// <summary>Puntos de una polilínea SVG (viewBox 0 0 <paramref name="width"/> × <paramref name="height"/>) con la serie normalizada.</summary>
    public static string PolylinePoints(IReadOnlyList<(DateOnly Day, double Value)> series, int width = 300, int height = 80, int pad = 6)
    {
        if (series.Count == 0)
        {
            return string.Empty;
        }

        var min = series.Min(s => s.Value);
        var max = series.Max(s => s.Value);
        var span = max - min;
        var points = series.Select((s, i) =>
        {
            var x = series.Count == 1 ? width / 2.0 : pad + i * (width - 2.0 * pad) / (series.Count - 1);
            var y = span <= 0 ? height / 2.0 : height - pad - (s.Value - min) / span * (height - 2.0 * pad);
            return string.Create(CultureInfo.InvariantCulture, $"{x:0.#},{y:0.#}");
        });
        return string.Join(' ', points);
    }

    /// <summary>"Mejor peso: 80 kg · el 12 mar" para la línea resumen de un ejercicio, o <c>null</c> si no hay récords.</summary>
    public static string? RecordsSummary(IEnumerable<PersonalRecordItem>? records)
    {
        var first = records?.FirstOrDefault();
        return first is null ? null : $"{RecordLabel(first.Kind)}: {FormatValue(first.Value, first.Unit)}";
    }
}
