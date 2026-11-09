namespace GSM.UI.Shared.Formatting;

/// <summary>
/// <c>GymType</c> del backend (<c>Gym=0, Box=1, Studio=2, PersonalTrainer=3, Academy=4, Other=5</c>): etiqueta y
/// una línea de qué cambia al elegirlo. El tipo es una plantilla de arranque y una etiqueta; la UI no decide
/// comportamiento con él (docs/GSM-tipos-de-negocio.md §2).
/// </summary>
public static class GymTypeLabels
{
    /// <summary>Nombre del tipo «Entrenador personal» como llega de la API (solo para textos y sugerencias, nunca para comportamiento).</summary>
    public const string PersonalTrainer = "PersonalTrainer";

    public static readonly (int Value, string Name, string Label, string Hint)[] All =
    [
        (0, "Gym", "Gimnasio", "Sala de máquinas y pesas. Configuración de siempre."),
        (1, "Box", "Box / entrenamiento funcional", "CrossFit, funcional, HIIT. Cartel de entrada fijo, logros de clases y volumen."),
        (2, "Studio", "Estudio", "Yoga, pilates, spinning, baile. Sin rutinas, cartel fijo, sin días de gracia, logros de clases."),
        (3, "PersonalTrainer", "Entrenador personal", "Sin QR, clases ni comunidad; revisión de rutinas cada 4 semanas, logros de sesiones de rutina."),
        (4, "Academy", "Academia", "Artes marciales, natación, escuelas deportivas. Sin rutinas, cartel fijo, logros de clases."),
        (5, "Other", "Otro", "Lo que no encaje en los demás."),
    ];

    /// <summary>
    /// E2: el tipo puede llegar como <c>PersonalTrainer</c> (nombre del enum de C#) o como <c>personal_trainer</c> (valor de Postgres):
    /// se comparan sin guiones bajos ni mayúsculas.
    /// </summary>
    private static bool Matches(string name, string? type)
        => string.Equals(name, (type ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase);

    public static string Label(string? type)
        => All.FirstOrDefault(t => Matches(t.Name, type)).Label ?? type ?? "Gimnasio";

    /// <summary>El valor entero del tipo a partir de su nombre (en cualquiera de los dos formatos); <c>0</c> (gimnasio) si no se reconoce.</summary>
    public static int ValueOf(string? name)
        => All.FirstOrDefault(t => Matches(t.Name, name)).Name is null ? 0 : All.First(t => Matches(t.Name, name)).Value;

    public static string Hint(int value) => All.FirstOrDefault(t => t.Value == value).Hint ?? string.Empty;
}
