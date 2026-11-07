using GSM.Domain.Achievements;
using GSM.Domain.Gyms;

namespace GSM.Application.Achievements;

/// <summary>
/// Catálogo de logros que se siembra al crear el gym, según su tipo (docs/GSM-tipos-de-negocio.md §4.5). Son cadenas
/// por nivel (bronce, plata, oro, platino, rubí), cada logro con su medalla en
/// <c>GSM.UI.Shared/wwwroot/achievements</c>. El gym puede editarlos, apagarlos o crear los suyos.
/// </summary>
public static class AchievementTemplates
{
    /// <summary>Ruta pública de las medallas del catálogo (recursos estáticos de GSM.UI.Shared).</summary>
    public const string IconBase = "/_content/GSM.UI.Shared/achievements/";

    public sealed record Seed(string Name, string Description, AchievementConditionType Type, int Value, string? Icon = null)
    {
        public string? IconUrl => Icon is null ? null : IconBase + Icon + ".webp";
    }

    // 1. Asistencia acumulada (visitas).
    private static readonly Seed[] Visits =
    [
        new("Primeros Pasos", "Tus primeras 5 visitas al gym", AchievementConditionType.Visits, 5, "primeros-pasos"),
        new("Constante", "10 visitas al gym", AchievementConditionType.Visits, 10, "constante"),
        new("Veterano", "25 visitas al gym", AchievementConditionType.Visits, 25, "veterano"),
        new("Medio Centenar", "50 visitas al gym", AchievementConditionType.Visits, 50, "medio-centenar"),
        new("Centurión", "100 visitas al gym", AchievementConditionType.Visits, 100, "centurion"),
        new("Leyenda del Club", "250 visitas al gym", AchievementConditionType.Visits, 250, "leyenda-del-club"),
    ];

    // 2. Constancia y rachas (días seguidos).
    private static readonly Seed[] Streaks =
    [
        new("Fuego Inicial", "3 días seguidos cumpliendo", AchievementConditionType.Streak, 3, "fuego-inicial"),
        new("Semana Perfecta", "7 días seguidos cumpliendo", AchievementConditionType.Streak, 7, "semana-perfecta"),
        new("Imparable", "14 días seguidos cumpliendo", AchievementConditionType.Streak, 14, "imparable"),
        new("Hábito de Hierro", "21 días seguidos cumpliendo", AchievementConditionType.Streak, 21, "habito-de-hierro"),
        new("Inquebrantable", "30 días seguidos cumpliendo", AchievementConditionType.Streak, 30, "inquebrantable"),
    ];

    // 3. Cumplimiento de rutinas: sesiones completas (todos los ejercicios del día registrados).
    private static readonly Seed[] Discipline =
    [
        new("Enfocado", "5 sesiones de rutina completas", AchievementConditionType.CompleteSessions, 5, "enfocado"),
        new("Disciplinado", "15 sesiones de rutina completas", AchievementConditionType.CompleteSessions, 15, "disciplinado"),
        new("Atleta Metódico", "30 sesiones de rutina completas", AchievementConditionType.CompleteSessions, 30, "atleta-metodico"),
        new("Maestro de la Rutina", "60 sesiones de rutina completas", AchievementConditionType.CompleteSessions, 60, "maestro-de-la-rutina"),
        new("Perfeccionista", "100 sesiones de rutina completas", AchievementConditionType.CompleteSessions, 100, "perfeccionista"),
    ];

    // 4. Clases dirigidas.
    private static readonly Seed[] Classes =
    [
        new("Curioso", "Asististe a 3 clases", AchievementConditionType.Classes, 3, "curioso"),
        new("Fan de las Clases", "Asististe a 10 clases", AchievementConditionType.Classes, 10, "fan-de-las-clases"),
        new("Compañero de Fila", "Asististe a 25 clases", AchievementConditionType.Classes, 25, "companero-de-fila"),
        new("Master de Estudio", "Asististe a 50 clases", AchievementConditionType.Classes, 50, "master-de-estudio"),
        new("Polifacético", "Asististe a 4 clases distintas", AchievementConditionType.DistinctClasses, 4, "polifacetico"),
    ];

    // 5. Cargas y récords (fuerza). El tonelaje es acumulado.
    private static readonly Seed[] Strength =
    [
        new("Rompiendo Barreras I", "Superaste tu primer récord personal", AchievementConditionType.PersonalRecords, 1, "rompiendo-barreras-1"),
        new("Rompiendo Barreras II", "Superaste 5 récords personales", AchievementConditionType.PersonalRecords, 5, "rompiendo-barreras-2"),
        new("Club de la Fuerza", "Registraste marca en sentadilla, press de banca y peso muerto", AchievementConditionType.PowerLifts, 3, "club-de-la-fuerza"),
        new("Tonelaje I", "Moviste 10.000 kg en total", AchievementConditionType.LiftedVolume, 10_000, "tonelaje-1"),
        new("Tonelaje II", "Moviste 100.000 kg en total", AchievementConditionType.LiftedVolume, 100_000, "tonelaje-2"),
        new("Hércules", "Moviste 500.000 kg en total", AchievementConditionType.LiftedVolume, 500_000, "hercules"),
    ];

    // 6. Horarios y hábitos (horas fijas: antes de las 8:00, desde las 20:00, sábado o domingo).
    private static readonly Seed[] Habits =
    [
        new("Madrugador I", "5 días entrenando antes de las 8:00", AchievementConditionType.EarlyVisits, 5, "madrugador-1"),
        new("Madrugador II", "20 días entrenando antes de las 8:00", AchievementConditionType.EarlyVisits, 20, "madrugador-2"),
        new("Noctámbulo I", "5 días entrenando desde las 20:00", AchievementConditionType.LateVisits, 5, "noctambulo-1"),
        new("Noctámbulo II", "20 días entrenando desde las 20:00", AchievementConditionType.LateVisits, 20, "noctambulo-2"),
        new("Guerrero de Fin de Semana", "Entrenaste 8 fines de semana", AchievementConditionType.Weekends, 8, "guerrero-de-fin-de-semana"),
    ];

    // 8. Ranking mensual (meses cerrados).
    private static readonly Seed[] Ranking =
    [
        new("En el Tablero", "Terminaste un mes en el top 50 del ranking", AchievementConditionType.MonthlyRankingTop, 50, "en-el-tablero"),
        new("Aspirante al Podio", "Terminaste un mes en el top 10 del ranking", AchievementConditionType.MonthlyRankingTop, 10, "aspirante-al-podio"),
        new("Medallero", "Terminaste un mes en el podio del ranking", AchievementConditionType.MonthlyRankingTop, 3, "medallero"),
        new("Número Uno", "Terminaste un mes en el primer puesto del ranking", AchievementConditionType.MonthlyRankingTop, 1, "numero-uno"),
    ];

    private static readonly Seed Tenure =
        new("1 año con nosotros", "12 meses como miembro", AchievementConditionType.TenureMonths, 12, "un-ano-con-nosotros");

    private static readonly Seed Referral =
        new("Primer amigo", "Un amigo que referiste ya se inscribió", AchievementConditionType.Referrals, 1, "primer-amigo");

    public static IReadOnlyList<Seed> For(GymType type)
    {
        var seeds = new List<Seed>();

        // El entrenador personal no tiene puerta: sin visitas, horarios ni ranking (lo apaga su plantilla).
        var hasDoor = type != GymType.PersonalTrainer;
        // Rutinas y fuerza: donde se entrena con rutina (el estudio y la academia giran alrededor de las clases).
        var hasRoutines = type is not (GymType.Studio or GymType.Academy);
        var hasClasses = type is GymType.Box or GymType.Studio or GymType.Academy;

        if (hasDoor)
        {
            seeds.AddRange(Visits);
        }

        seeds.AddRange(Streaks);

        if (hasRoutines)
        {
            seeds.AddRange(Discipline);
            seeds.AddRange(Strength);
        }

        if (hasClasses)
        {
            seeds.AddRange(Classes);
        }

        if (hasDoor)
        {
            seeds.AddRange(Habits);
            seeds.AddRange(Ranking);
        }

        seeds.Add(Tenure);
        seeds.Add(Referral);
        return seeds;
    }
}
