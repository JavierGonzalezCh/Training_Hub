using GSM.Domain.Achievements;
using GSM.Domain.Gyms;

namespace GSM.Application.Achievements;

/// <summary>Catálogo de logros que se siembra al crear el gym, según su tipo (docs/GSM-tipos-de-negocio.md §4.5).</summary>
public static class AchievementTemplates
{
    public sealed record Seed(string Name, string Description, AchievementConditionType Type, int Value);

    private static readonly Seed[] Visits =
    [
        new("Primera visita", "Tu primer check-in", AchievementConditionType.Visits, 1),
        new("10 visitas", "10 check-ins acumulados", AchievementConditionType.Visits, 10),
        new("50 visitas", "50 check-ins acumulados", AchievementConditionType.Visits, 50),
    ];

    private static readonly Seed[] Streaks =
    [
        new("Racha de 7 días", "7 días seguidos entrenando", AchievementConditionType.Streak, 7),
        new("Racha de 30 días", "30 días seguidos entrenando", AchievementConditionType.Streak, 30),
    ];

    private static readonly Seed Tenure =
        new("1 año con nosotros", "12 meses como miembro", AchievementConditionType.TenureMonths, 12);

    private static readonly Seed FirstRecord =
        new("Primer récord", "Batiste un récord personal", AchievementConditionType.PersonalRecords, 1);

    private static readonly Seed Referral =
        new("Primer amigo", "Un amigo que referiste ya se inscribió", AchievementConditionType.Referrals, 1);

    private static readonly Seed[] Classes =
    [
        new("10 clases", "Asististe a 10 clases", AchievementConditionType.Classes, 10),
        new("50 clases", "Asististe a 50 clases", AchievementConditionType.Classes, 50),
    ];

    private static readonly Seed[] RoutineSessions =
    [
        new("10 sesiones de rutina", "Registraste 10 días de entrenamiento", AchievementConditionType.RoutineSessions, 10),
        new("50 sesiones de rutina", "Registraste 50 días de entrenamiento", AchievementConditionType.RoutineSessions, 50),
    ];

    private static readonly Seed Volume =
        new("Volumen: 10.000 kg", "Levantaste 10.000 kg en total", AchievementConditionType.LiftedVolume, 10_000);

    public static IReadOnlyList<Seed> For(GymType type)
    {
        var seeds = new List<Seed>();

        // Para el entrenador personal las «visitas» no significan nada: cuentan las sesiones de rutina.
        if (type != GymType.PersonalTrainer)
        {
            seeds.AddRange(Visits);
        }

        seeds.AddRange(Streaks);
        seeds.Add(Tenure);

        if (type is not (GymType.Studio or GymType.Academy))
        {
            seeds.Add(FirstRecord);
        }

        seeds.Add(Referral);

        if (type is GymType.Box or GymType.Studio or GymType.Academy)
        {
            seeds.AddRange(Classes);
        }

        if (type == GymType.PersonalTrainer)
        {
            seeds.AddRange(RoutineSessions);
        }

        if (type is GymType.Box or GymType.PersonalTrainer)
        {
            seeds.Add(Volume);
        }

        return seeds;
    }
}
