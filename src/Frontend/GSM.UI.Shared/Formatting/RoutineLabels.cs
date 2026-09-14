namespace GSM.UI.Shared.Formatting;

/// <summary>Catálogos cerrados de <c>Muscle</c> y <c>Equipment</c> (ver <c>GSM.Domain.Routines</c>) en español.</summary>
public static class RoutineLabels
{
    public static readonly (int Value, string Label)[] Muscles =
    [
        (0, "Pecho"), (1, "Espalda"), (2, "Hombros"), (3, "Bíceps"), (4, "Tríceps"),
        (5, "Antebrazos"), (6, "Cuádriceps"), (7, "Isquiotibiales"), (8, "Glúteos"),
        (9, "Pantorrillas"), (10, "Abdomen"), (11, "Lumbar"), (12, "Cuello"),
        (13, "Cuerpo completo"), (14, "Cardio"),
    ];

    public static readonly (int Value, string Label)[] Equipment =
    [
        (0, "Barra"), (1, "Mancuerna"), (2, "Kettlebell"), (3, "Máquina"), (4, "Polea"),
        (5, "Máquina Smith"), (6, "Banco"), (7, "Rack"), (8, "Banda elástica"), (9, "TRX"),
        (10, "Balón medicinal"), (11, "Colchoneta"), (12, "Cajón"), (13, "Cuerda de saltar"),
        (14, "Peso corporal"), (15, "Máquina de cardio"),
    ];

    private static readonly Dictionary<string, string> MuscleByName = new()
    {
        ["Chest"] = "Pecho", ["Back"] = "Espalda", ["Shoulders"] = "Hombros", ["Biceps"] = "Bíceps",
        ["Triceps"] = "Tríceps", ["Forearms"] = "Antebrazos", ["Quadriceps"] = "Cuádriceps",
        ["Hamstrings"] = "Isquiotibiales", ["Glutes"] = "Glúteos", ["Calves"] = "Pantorrillas",
        ["Abs"] = "Abdomen", ["Lower_back"] = "Lumbar", ["Neck"] = "Cuello",
        ["Full_body"] = "Cuerpo completo", ["Cardio"] = "Cardio",
    };

    private static readonly Dictionary<string, string> EquipmentByName = new()
    {
        ["Barbell"] = "Barra", ["Dumbbell"] = "Mancuerna", ["Kettlebell"] = "Kettlebell",
        ["Machine"] = "Máquina", ["Cable"] = "Polea", ["Smith_machine"] = "Máquina Smith",
        ["Bench"] = "Banco", ["Rack"] = "Rack", ["Resistance_band"] = "Banda elástica",
        ["Trx"] = "TRX", ["Medicine_ball"] = "Balón medicinal", ["Mat"] = "Colchoneta",
        ["Box"] = "Cajón", ["Jump_rope"] = "Cuerda de saltar", ["Bodyweight"] = "Peso corporal",
        ["Cardio_machine"] = "Máquina de cardio",
    };

    public static string MuscleLabel(string name) => MuscleByName.GetValueOrDefault(name, name);

    public static string EquipmentLabel(string name) => EquipmentByName.GetValueOrDefault(name, name);
}
