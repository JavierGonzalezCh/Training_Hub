using GSM.Application.Gyms.Contracts;
using GSM.Application.Memberships.Contracts;

namespace GSM.Application.Dashboard;

/// <summary>
/// Inicio del miembro (Dashboards §2, D2): un objeto con una propiedad por bloque, en el orden del documento.
/// Un bloque que el gym tiene apagado, o cuyo lector falló, viaja <c>null</c> (no 403) y el resto llega; los
/// bloques que aplican pero están vacíos viajan con listas vacías o campos nulos.
/// </summary>
public sealed record MemberHomeResponse(
    HomeStatusBlock? Status,
    HomeGymBlock? Gym,
    HomeEntryBlock? Entry,
    HomePendingBlock? Pending,
    HomeTrainingBlock? Training,
    HomeNextClassBlock? NextClass,
    HomeProgressBlock? Progress,
    HomeNewsBlock? News);

/// <summary>1. Saludo y estado: el veredicto comercial de hoy (con el letrero) y la membresía que cubre hoy, si la hay.</summary>
/// <param name="Access">Estado comercial, días que quedan y letrero (Memberships §5.3); el mismo de "Mi membresía".</param>
/// <param name="PlanName">Plan de la membresía que cubre hoy; nulo si ninguna cubre hoy.</param>
public sealed record HomeStatusBlock(
    AccessStatusResponse Access,
    string? PlanName,
    DateOnly? StartDate,
    DateOnly? EndDate);

/// <summary>2. El gym ahora: abierto o cerrado y qué tan lleno está.</summary>
/// <param name="OpenStatus">Abierto/cerrado con el texto ya redactado; sin horario cargado, <c>HasSchedule</c> es falso.</param>
/// <param name="OccupancyLevel"><c>calm</c>, <c>moderate</c> o <c>full</c>; nulo si el gym no configuró aforo.</param>
public sealed record HomeGymBlock(GymOpenStatusDto OpenStatus, string? OccupancyLevel);

/// <summary>3. Entrada: si ya entró hoy y a qué hora (primera entrada válida del día).</summary>
public sealed record HomeEntryBlock(bool CheckedInToday, DateTimeOffset? CheckedInAt);

/// <summary>4. Pendientes: ofertas de la lista de espera por confirmar y premios por reclamar.</summary>
public sealed record HomePendingBlock(
    IReadOnlyList<HomeWaitlistOffer> WaitlistOffers,
    IReadOnlyList<HomeReward> Rewards);

public sealed record HomeWaitlistOffer(
    Guid ClassId,
    string ClassName,
    DateOnly SessionDate,
    TimeOnly StartTime,
    DateTimeOffset ExpiresAt);

public sealed record HomeReward(
    Guid AchievementId,
    string AchievementName,
    string RewardDescription,
    DateTimeOffset? ExpiresAt);

/// <summary>5. Tu entrenamiento de hoy. Nulo si el gym apagó las rutinas o el miembro no tiene rutina activa.</summary>
/// <param name="DayName">Nombre del día que toca, o "Descanso".</param>
/// <param name="MuscleGroups">Grupos musculares (nombres del enum) del día.</param>
/// <param name="ReviewOn">Fecha de revisión, solo si la rutina la armó el staff y cae dentro de los próximos 7 días (o ya pasó).</param>
/// <param name="ReviewDaysLeft">Días que faltan para la revisión; 0 = hoy; negativo = vencida. Nulo cuando <c>ReviewOn</c> es nulo.</param>
public sealed record HomeTrainingBlock(
    string RoutineName,
    string DayName,
    bool IsRest,
    int ExerciseCount,
    IReadOnlyList<string> MuscleGroups,
    DateOnly? ReviewOn,
    int? ReviewDaysLeft);

/// <summary>6. Tu próxima clase: la siguiente reserva. Nulo si el gym apagó las clases o no tiene reservas.</summary>
public sealed record HomeNextClassBlock(
    Guid ClassId,
    string ClassName,
    DateOnly SessionDate,
    TimeOnly StartTime,
    string? InstructorName);

/// <summary>7. Tu progreso: racha, congeladores y, si participa, su puesto en el ranking del mes.</summary>
/// <param name="Ranking">Nulo si el gym apagó el ranking o la gamificación.</param>
public sealed record HomeProgressBlock(
    int CurrentStreak,
    int FreezesAvailable,
    int TotalVisits,
    HomeRankingStanding? Ranking);

/// <param name="Position">Puesto del mes; nulo si no participa o todavía no tiene días en el mes.</param>
public sealed record HomeRankingStanding(bool Participating, int? Position, int Days);

/// <summary>8. Novedades: el anuncio destacado (fijado) y cuántos anuncios no ha visto. Nulo si el gym apagó las comunicaciones.</summary>
public sealed record HomeNewsBlock(HomeFeaturedAnnouncement? Featured, int UnreadAnnouncements);

public sealed record HomeFeaturedAnnouncement(
    Guid Id,
    string Title,
    bool Pinned,
    bool Important,
    DateTimeOffset PublishedAt);
