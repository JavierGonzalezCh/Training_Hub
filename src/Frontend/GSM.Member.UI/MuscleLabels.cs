namespace GSM.Member.UI;

/// <summary>Nombres en español de los músculos del catálogo (enum <c>Muscle</c> del dominio, que viaja como texto).</summary>
public static class MuscleLabels
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chest"] = "pecho",
        ["Back"] = "espalda",
        ["Shoulders"] = "hombros",
        ["Biceps"] = "bíceps",
        ["Triceps"] = "tríceps",
        ["Forearms"] = "antebrazos",
        ["Quadriceps"] = "cuádriceps",
        ["Hamstrings"] = "isquiotibiales",
        ["Glutes"] = "glúteos",
        ["Calves"] = "pantorrillas",
        ["Abs"] = "abdomen",
        ["Lower_back"] = "lumbar",
        ["Neck"] = "cuello",
        ["Full_body"] = "cuerpo completo",
        ["Traps"] = "trapecio",
        ["Obliques"] = "oblicuos",
        ["Adductors"] = "aductores",
        ["Abductors"] = "abductores",
    };

    public static string Label(string muscle) => Names.TryGetValue(muscle, out var name) ? name : muscle;

    /// <summary>«pecho, tríceps y hombros».</summary>
    public static string Join(IEnumerable<string> muscles)
    {
        var list = muscles.Select(Label).Distinct().ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " y " + list[^1],
        };
    }

    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

/// <summary>
/// Material visual de muestra (Unsplash) mientras el gym no carga fotos propias: una imagen por músculo principal y una
/// para el día. Solo se usa cuando el ejercicio no trae <c>ImageUrl</c>. Temporal: se retira cuando haya fotos reales.
/// </summary>
public static class PlaceholderMedia
{
    private const string Base = "https://images.unsplash.com/";

    private static readonly Dictionary<string, string> ByMuscle = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chest"] = "photo-1571019614242-c5c5dee9f50b",
        ["Back"] = "photo-1603287681836-b174ce5074c2",
        ["Shoulders"] = "photo-1532029837206-abbe2b7620e3",
        ["Biceps"] = "photo-1581009146145-b5ef050c2e1e",
        ["Triceps"] = "photo-1530822847156-5df684ec5ee1",
        ["Forearms"] = "photo-1581009146145-b5ef050c2e1e",
        ["Quadriceps"] = "photo-1574680096145-d05b474e2155",
        ["Hamstrings"] = "photo-1599058917212-d750089bc07e",
        ["Glutes"] = "photo-1583454110551-21f2fa2afe61",
        ["Calves"] = "photo-1434608519344-49d77a699e1d",
        ["Abs"] = "photo-1571019613454-1cb2f99b2d8b",
        ["Obliques"] = "photo-1571019613454-1cb2f99b2d8b",
        ["Lower_back"] = "photo-1599058917212-d750089bc07e",
        ["Traps"] = "photo-1603287681836-b174ce5074c2",
        ["Adductors"] = "photo-1434608519344-49d77a699e1d",
        ["Abductors"] = "photo-1583454110551-21f2fa2afe61",
    };

    public const string Gym = Base + "photo-1534438327276-14e5300c3a48?w=900&q=70&auto=format&fit=crop";

    public static string Exercise(string? imageUrl, string? primaryMuscle, int width = 300)
        => !string.IsNullOrWhiteSpace(imageUrl)
            ? imageUrl
            : $"{Base}{(primaryMuscle is not null && ByMuscle.TryGetValue(primaryMuscle, out var id) ? id : "photo-1517836357463-d25dfeac3438")}?w={width}&q=60&auto=format&fit=crop";
}
