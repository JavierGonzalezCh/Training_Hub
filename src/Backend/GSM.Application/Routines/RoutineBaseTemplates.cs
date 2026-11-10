namespace GSM.Application.Routines;

/// <summary>
/// Las plantillas base de rutina (RT1): cinco rutinas listas que cada gym recibe como copias editables. Los
/// ejercicios se nombran como en el catálogo del sistema (sus ids cambian entre bases); al sembrar se resuelven por
/// nombre y una línea cuyo ejercicio no exista se omite. Varias piden barra, rack, poleas o máquinas a propósito: si el
/// gym no las tiene, la plantilla lo avisa.
/// </summary>
public static class RoutineBaseTemplates
{
    public sealed record Line(string Exercise, IReadOnlyList<Set> Sets, int RestSeconds, string? Notes = null);

    public sealed record Set(int? RepsMin = null, int? RepsMax = null, int? DurationSeconds = null);

    public sealed record Day(string Name, IReadOnlyList<Line> Lines);

    /// <summary><c>Image</c>: clave de la portada en <see cref="RoutineImageGallery"/>.</summary>
    public sealed record Template(string Key, string Name, string Description, string Image, IReadOnlyList<Day> Days)
    {
        public string ImageUrl => RoutineImageGallery.UrlOf(Image);
    }

    private static IReadOnlyList<Set> Reps(int sets, int min, int? max = null)
        => Enumerable.Range(0, sets).Select(_ => new Set(min, max ?? min)).ToList();

    private static IReadOnlyList<Set> Seconds(int sets, int seconds)
        => Enumerable.Range(0, sets).Select(_ => new Set(DurationSeconds: seconds)).ToList();

    private static Line L(string exercise, IReadOnlyList<Set> sets, int rest = 90, string? notes = null)
        => new(exercise, sets, rest, notes);

    public static readonly IReadOnlyList<Template> All =
    [
        new("full-body-principiante", "Full body principiante",
            "Tres días de cuerpo completo con máquinas y mancuernas. Para quien empieza o vuelve a entrenar.",
            "sala-mancuernas",
            [
                new("Día A", [
                    L("Prensa de piernas", Reps(3, 10, 12)),
                    L("Press de banca inclinado con mancuernas", Reps(3, 10, 12)),
                    L("Jalón al pecho en polea", Reps(3, 10, 12)),
                    L("Elevaciones laterales", Reps(3, 12, 15), 60),
                    L("Plancha", Seconds(3, 30), 45),
                ]),
                new("Día B", [
                    L("Sentadilla búlgara", Reps(3, 8, 10)),
                    L("Remo en máquina", Reps(3, 10, 12)),
                    L("Press de hombros con mancuernas", Reps(3, 10, 12)),
                    L("Curl femoral tumbado en máquina", Reps(3, 12), 60),
                    L("Crunch abdominal", Reps(3, 15), 45),
                ]),
                new("Día C", [
                    L("Extensión de cuádriceps en máquina", Reps(3, 12, 15), 60),
                    L("Remo con mancuerna a una mano", Reps(3, 10)),
                    L("Flexiones de pecho", Reps(3, 10), 60),
                    L("Puente de glúteos", Reps(3, 15), 60),
                    L("Bicicleta estática", Seconds(1, 600), 0, "Ritmo suave para terminar."),
                ]),
            ]),
        new("torso-pierna", "Torso / Pierna",
            "Cuatro días alternando tren superior e inferior. Intermedio, con barra y poleas.",
            "prensa-piernas",
            [
                new("Torso A", [
                    L("Press de banca plana con barra", Reps(4, 6, 8), 150),
                    L("Remo con barra", Reps(4, 8), 120),
                    L("Press militar con barra", Reps(3, 8, 10), 120),
                    L("Jalón al pecho en polea", Reps(3, 10)),
                    L("Curl de bíceps con barra", Reps(3, 10), 60),
                    L("Extensión de tríceps en polea", Reps(3, 12), 60),
                ]),
                new("Pierna A", [
                    L("Sentadilla con barra", Reps(4, 6, 8), 180),
                    L("Peso muerto rumano", Reps(3, 8, 10), 120),
                    L("Prensa de piernas", Reps(3, 12)),
                    L("Curl femoral tumbado en máquina", Reps(3, 12), 60),
                    L("Elevación de talones de pie", Reps(4, 12, 15), 60),
                ]),
                new("Torso B", [
                    L("Press de banca inclinado con mancuernas", Reps(4, 8, 10), 120),
                    L("Dominadas", Reps(4, 6, 10), 120),
                    L("Press de hombros con mancuernas", Reps(3, 10)),
                    L("Cruce de poleas", Reps(3, 12), 60),
                    L("Curl martillo", Reps(3, 12), 60),
                    L("Press francés con barra", Reps(3, 10), 60),
                ]),
                new("Pierna B", [
                    L("Peso muerto convencional", Reps(4, 5), 180),
                    L("Zancadas con mancuernas", Reps(3, 10)),
                    L("Extensión de cuádriceps en máquina", Reps(3, 12), 60),
                    L("Hip thrust con barra", Reps(3, 10)),
                    L("Elevación de talones sentado", Reps(3, 15), 60),
                ]),
            ]),
        new("push-pull-legs", "Push / Pull / Legs",
            "Empuje, tirón y pierna en tres días que se repiten. Intermedio a avanzado.",
            "press-banca",
            [
                new("Push", [
                    L("Press de banca plana con barra", Reps(4, 6, 8), 150),
                    L("Press de banca inclinado con mancuernas", Reps(3, 8, 10), 120),
                    L("Press militar con barra", Reps(3, 8), 120),
                    L("Elevaciones laterales", Reps(3, 12, 15), 60),
                    L("Extensión de tríceps en polea", Reps(3, 12), 60),
                    L("Fondos en paralelas", Reps(3, 8, 12)),
                ]),
                new("Pull", [
                    L("Dominadas", Reps(4, 6, 10), 120),
                    L("Remo con barra", Reps(4, 8), 120),
                    L("Jalón al pecho en polea", Reps(3, 10, 12)),
                    L("Face pull en polea", Reps(3, 15), 60),
                    L("Curl de bíceps con barra", Reps(3, 10), 60),
                    L("Curl martillo", Reps(3, 12), 60),
                ]),
                new("Legs", [
                    L("Sentadilla con barra", Reps(4, 6, 8), 180),
                    L("Peso muerto rumano", Reps(3, 8, 10), 120),
                    L("Prensa de piernas", Reps(3, 10, 12)),
                    L("Curl femoral tumbado en máquina", Reps(3, 12), 60),
                    L("Elevación de talones de pie", Reps(4, 12), 60),
                ]),
            ]),
        new("fuerza-5x5", "Fuerza 5×5",
            "Dos días que se alternan con los básicos de barra. Sube el peso cuando completes las cinco series.",
            "peso-muerto",
            [
                new("Día A", [
                    L("Sentadilla con barra", Reps(5, 5), 180),
                    L("Press de banca plana con barra", Reps(5, 5), 180),
                    L("Remo con barra", Reps(5, 5), 150),
                ]),
                new("Día B", [
                    L("Sentadilla con barra", Reps(5, 5), 180),
                    L("Press militar con barra", Reps(5, 5), 180),
                    L("Peso muerto convencional", Reps(1, 5), 180, "Una sola serie pesada tras calentar."),
                ]),
            ]),
        new("cardio-core", "Cardio + core",
            "Dos días de resistencia y abdomen, con poco equipo. Sirve para bajar de peso o como complemento.",
            "cintas-correr",
            [
                new("Día A", [
                    L("Cinta de correr", Seconds(1, 1200), 0, "Ritmo en que puedas hablar."),
                    L("Plancha", Seconds(3, 45), 45),
                    L("Crunch abdominal", Reps(3, 20), 45),
                    L("Escaladores (mountain climbers)", Seconds(3, 30), 45),
                    L("Burpees", Reps(3, 10), 60),
                ]),
                new("Día B", [
                    L("Bicicleta estática", Seconds(1, 1500), 0),
                    L("Elevación de piernas colgado", Reps(3, 12), 60),
                    L("Rueda abdominal", Reps(3, 10), 60),
                    L("Salto a la cuerda", Seconds(3, 60), 45),
                    L("Puente de glúteos", Reps(3, 15), 45),
                ]),
            ]),
    ];
}
