using System.Globalization;
using GSM.Client.Generated;

namespace GSM.UI.Shared.Routines;

/// <summary>Qué campos de una serie corresponden a lo que se mide en el ejercicio (<c>tracking_type</c>).</summary>
public readonly record struct TrackingFields(bool Reps, bool Weight, bool Time, bool Distance)
{
    /// <summary>Nombre que viaja en las respuestas (<c>RepsWeight</c>, <c>Reps</c>, <c>Time</c>, <c>Distance</c>, <c>TimeDistance</c>, <c>WeightDistance</c>). Lo desconocido se trata como repeticiones y peso.</summary>
    public static TrackingFields Of(string? tracking) => tracking switch
    {
        "Reps" => new(true, false, false, false),
        "Time" => new(false, false, true, false),
        "Distance" => new(false, false, false, true),
        "TimeDistance" => new(false, false, true, true),
        "WeightDistance" => new(false, true, false, true),
        _ => new(true, true, false, false),
    };
}

/// <summary>Resultado de comparar una serie hecha contra su objetivo.</summary>
public enum SetOutcome
{
    /// <summary>La serie no estaba planeada (serie extra).</summary>
    Extra,

    /// <summary>Planeada y sin datos hechos.</summary>
    NotDone,

    Below,
    Met,
    Exceeded,
}

/// <summary>Lo que el miembro hizo en una serie, en los tipos que usa la pantalla.</summary>
public sealed record DoneSet(int? Reps, double? WeightKg, int? DurationSeconds, int? DistanceM, int? Rir = null);

/// <summary>Textos y comparación planeado vs hecho de las series (compartido por el panel y la app del miembro).</summary>
public static class SetPlan
{
    public static readonly (string Value, string Label)[] SetTypes =
    [
        ("normal", "Normal"), ("warmup", "Calentamiento"), ("drop", "Descendente"), ("failure", "Al fallo"),
    ];

    public static string SetTypeLabel(string? type)
        => SetTypes.FirstOrDefault(t => t.Value == (type ?? "normal").ToLowerInvariant()).Label ?? "Normal";

    /// <summary>"10", "8–12" o "al fallo". Vacío si la serie no lleva repeticiones.</summary>
    public static string RepsText(SetTargetResponse target)
    {
        if (string.Equals(target.SetType, "failure", StringComparison.OrdinalIgnoreCase))
        {
            return "al fallo";
        }

        return (target.RepsMin, target.RepsMax) switch
        {
            (null, null) => string.Empty,
            ({ } min, { } max) when min != max => $"{min}–{max}",
            ({ } min, _) => min.ToString(CultureInfo.InvariantCulture),
            (_, { } max) => max.ToString(CultureInfo.InvariantCulture),
        };
    }

    public static string DurationText(int seconds)
        => seconds >= 120 && seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds} s";

    public static string DistanceText(int meters)
        => meters >= 1000 && meters % 100 == 0
            ? $"{(meters / 1000d).ToString("0.#", CultureInfo.InvariantCulture)} km"
            : $"{meters} m";

    /// <summary>
    /// Objetivo de una serie en una línea: "8–12 · 40 kg · RIR 2". En un ejercicio unilateral las repeticiones
    /// son por lado ("10 por lado").
    /// </summary>
    public static string Describe(SetTargetResponse target, bool unilateral = false)
    {
        var parts = new List<string>();
        var reps = RepsText(target);
        if (reps.Length > 0)
        {
            parts.Add(unilateral && reps != "al fallo" ? $"{reps} por lado" : reps);
        }

        if (target.WeightKg is { } kg)
        {
            parts.Add($"{kg.ToString("0.##", CultureInfo.InvariantCulture)} kg");
        }

        if (target.DurationSeconds is { } seconds)
        {
            parts.Add(DurationText(seconds));
        }

        if (target.DistanceM is { } meters)
        {
            parts.Add(DistanceText(meters));
        }

        if (target.Rir is { } rir)
        {
            parts.Add($"RIR {rir}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Resumen de las series de una línea, agrupando las consecutivas idénticas:
    /// "Calentamiento 1 × 12 · 20 kg; 3 × 8–12 · 40 kg · RIR 2".
    /// </summary>
    public static string Summary(IEnumerable<SetTargetResponse>? targets, bool unilateral = false)
    {
        var runs = new List<(SetTargetResponse First, int Count)>();
        foreach (var t in (targets ?? []).OrderBy(t => t.Number))
        {
            if (runs.Count > 0 && Same(runs[^1].First, t))
            {
                runs[^1] = (runs[^1].First, runs[^1].Count + 1);
            }
            else
            {
                runs.Add((t, 1));
            }
        }

        return string.Join("; ", runs.Select(r =>
        {
            var text = Describe(r.First, unilateral);
            var prefix = IsNormal(r.First) ? string.Empty : SetTypeLabel(r.First.SetType) + " ";
            return text.Length == 0 ? $"{prefix}{r.Count} × serie" : $"{prefix}{r.Count} × {text}";
        }));
    }

    private static bool IsNormal(SetTargetResponse t) => string.Equals(t.SetType, "normal", StringComparison.OrdinalIgnoreCase);

    private static bool Same(SetTargetResponse a, SetTargetResponse b)
        => a.SetType == b.SetType && a.RepsMin == b.RepsMin && a.RepsMax == b.RepsMax && a.WeightKg == b.WeightKg
           && a.DurationSeconds == b.DurationSeconds && a.DistanceM == b.DistanceM && a.Rir == b.Rir;

    /// <summary>
    /// Compara una serie hecha con su objetivo. Cada medida con objetivo se juzga por separado (repeticiones contra el
    /// rango, peso, tiempo y distancia contra su valor); gana lo peor: una medida por debajo la deja "Por debajo". Si
    /// ninguna está por debajo y alguna superó el objetivo, "Superada". Una serie al fallo se cumple con al menos una
    /// repetición.
    /// </summary>
    public static SetOutcome Compare(SetTargetResponse? target, DoneSet? done)
    {
        if (target is null)
        {
            return SetOutcome.Extra;
        }

        if (done is null || (done.Reps is null && done.WeightKg is null && done.DurationSeconds is null && done.DistanceM is null))
        {
            return SetOutcome.NotDone;
        }

        var results = new List<SetOutcome>();
        if (string.Equals(target.SetType, "failure", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(done.Reps is >= 1 ? SetOutcome.Met : SetOutcome.Below);
        }
        else if (target.RepsMin is { } min)
        {
            var max = target.RepsMax ?? min;
            results.Add(done.Reps is not { } reps || reps < min ? SetOutcome.Below : reps > max ? SetOutcome.Exceeded : SetOutcome.Met);
        }

        if (target.WeightKg is { } plannedKg)
        {
            results.Add(Measure(done.WeightKg, plannedKg));
        }

        if (target.DurationSeconds is { } plannedSeconds)
        {
            results.Add(Measure(done.DurationSeconds, plannedSeconds));
        }

        if (target.DistanceM is { } plannedMeters)
        {
            results.Add(Measure(done.DistanceM, plannedMeters));
        }

        if (results.Count == 0)
        {
            return SetOutcome.Met;
        }

        return results.Contains(SetOutcome.Below) ? SetOutcome.Below
            : results.Contains(SetOutcome.Exceeded) ? SetOutcome.Exceeded
            : SetOutcome.Met;
    }

    private static SetOutcome Measure(double? done, double planned)
        => done is not { } value || value < planned ? SetOutcome.Below : value > planned ? SetOutcome.Exceeded : SetOutcome.Met;

    /// <summary>Compara serie contra serie por posición; las hechas de más son extras y las planeadas sin hacer, "sin hacer".</summary>
    public static IReadOnlyList<SetOutcome> CompareAll(IEnumerable<SetTargetResponse>? targets, IReadOnlyList<DoneSet> done)
    {
        var ordered = (targets ?? []).OrderBy(t => t.Number).ToList();
        return Enumerable.Range(0, Math.Max(ordered.Count, done.Count))
            .Select(i => Compare(i < ordered.Count ? ordered[i] : null, i < done.Count ? done[i] : null))
            .ToList();
    }

    public static string OutcomeLabel(SetOutcome outcome) => outcome switch
    {
        SetOutcome.Met => "Cumplida",
        SetOutcome.Exceeded => "Superada",
        SetOutcome.Below => "Por debajo",
        SetOutcome.NotDone => "Sin hacer",
        _ => "Extra",
    };

    /// <summary>"3 de 4 series cumplidas" (cuentan como cumplidas las cumplidas y las superadas).</summary>
    public static string ProgressText(IReadOnlyList<SetOutcome> outcomes)
    {
        var planned = outcomes.Count(o => o != SetOutcome.Extra);
        var reached = outcomes.Count(o => o is SetOutcome.Met or SetOutcome.Exceeded);
        return $"{reached} de {planned} series cumplidas";
    }

    /// <summary>
    /// Letra y orden de cada línea que forma parte de una superserie o circuito, en el orden dado: "A1", "A2", "B1"…
    /// Las líneas sueltas no aparecen.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> GroupLabels(IEnumerable<RoutineExerciseResponse> lines)
    {
        var all = lines.ToList();
        var result = new Dictionary<Guid, string>();
        var letters = new Dictionary<int, char>();
        var counts = new Dictionary<int, int>();
        foreach (var line in all)
        {
            if (line.Group is not { } group)
            {
                continue;
            }

            if (!letters.TryGetValue(group, out var letter))
            {
                letter = (char)('A' + letters.Count % 26);
                letters[group] = letter;
            }

            counts[group] = counts.GetValueOrDefault(group) + 1;
            result[line.Id] = $"{letter}{counts[group]}";
        }

        // Un grupo de una sola línea no es superserie.
        foreach (var single in counts.Where(c => c.Value == 1).Select(c => c.Key))
        {
            foreach (var line in all.Where(l => l.Group == single))
            {
                result.Remove(line.Id);
            }
        }

        return result;
    }
}
