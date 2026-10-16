namespace GSM.Application.MemberStreak;

/// <summary>
/// "Mi progreso" → Racha (L7, Achievements v2 §8): la racha de un mes explicada día a día, la mejor racha, qué toca hoy
/// y los congeladores.
/// </summary>
/// <param name="TodayKind">
/// <c>trained</c> (ya entrenó hoy), <c>train</c> (hoy toca entrenar), <c>rest</c> (descanso planificado) o <c>free</c>
/// (sin rutina vigente: no hay calendario que decir).
/// </param>
/// <param name="HasRoutine">Si hay una rutina vigente que marca los días de entrenamiento y de descanso.</param>
/// <param name="FreezesAvailable">Congeladores disponibles.</param>
/// <param name="NextFreezeOn">Cuándo gana el siguiente congelador; nulo si ya tiene el máximo.</param>
/// <param name="FreezesUsedOn">Los días que salvó un congelador, el más reciente primero.</param>
/// <param name="HistoryStart">El primer día con datos (primera entrada o inicio de su calendario); nulo si no hay. Hasta ahí se puede ir hacia atrás.</param>
/// <param name="Days">Un elemento por cada día del mes, en orden.</param>
public sealed record StreakCalendarResponse(
    int Year,
    int Month,
    int CurrentStreak,
    int BestStreak,
    string TodayKind,
    bool HasRoutine,
    int FreezesAvailable,
    DateOnly? NextFreezeOn,
    IReadOnlyList<DateOnly> FreezesUsedOn,
    DateOnly? HistoryStart,
    IReadOnlyList<StreakCalendarDay> Days);

/// <param name="Kind">
/// <c>trained</c> (entrenó), <c>rest</c> (descanso planificado), <c>frozen</c> (lo salvó un congelador),
/// <c>missed</c> (tocaba entrenar y no fue), <c>pending</c> (hoy toca y aún no va) o <c>none</c> (sin nada que decir:
/// futuro, o fuera de toda rutina).
/// </param>
/// <param name="Note">Por qué una entrada de ese día no contó (sin ubicación o fuera del radio con QR fijo); nulo si no aplica.</param>
public sealed record StreakCalendarDay(DateOnly Date, string Kind, string? Note);
