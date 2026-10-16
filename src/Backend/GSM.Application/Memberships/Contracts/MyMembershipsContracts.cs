namespace GSM.Application.Memberships.Contracts;

/// <summary>
/// "Mi membresía" en la app del miembro (MS8, Memberships v2 §11): el veredicto de hoy y el historial completo de sus
/// membresías, con sus congelamientos y las entradas que consumió de cada tiquetera. Sin dinero ni motivos internos
/// del staff (precio acordado, descuentos, motivo de cancelación).
/// </summary>
/// <param name="Access">Estado comercial y letrero de hoy (el mismo que ve recepción).</param>
/// <param name="Items">Todas las membresías, la más reciente primero.</param>
public sealed record MyMembershipsResponse(AccessStatusResponse Access, IReadOnlyList<MyMembershipItem> Items);

/// <param name="Kind"><c>time</c> o <c>pass</c> (tiquetera).</param>
/// <param name="Status"><c>Active</c>, <c>Expired</c>, <c>Cancelled</c> o <c>Paused</c> (congelada hoy), según el día de hoy.</param>
/// <param name="EntriesTotal">Solo tiqueteras: entradas con que empezó.</param>
/// <param name="EntriesUsed">Solo tiqueteras.</param>
/// <param name="EntriesLeft">Solo tiqueteras.</param>
/// <param name="IsScheduled">Aún no empieza: pagada por adelantado, empieza después de hoy.</param>
/// <param name="IsGroup">Si es de un plan de grupo (familiar, pareja, corporativo).</param>
/// <param name="Freezes">Sus congelamientos, el más antiguo primero.</param>
/// <param name="Entries">Solo tiqueteras: los días en que gastó una entrada, el más antiguo primero.</param>
public sealed record MyMembershipItem(
    Guid Id,
    string PlanName,
    string Kind,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    int? EntriesTotal,
    int? EntriesUsed,
    int? EntriesLeft,
    bool IsScheduled,
    bool IsGroup,
    IReadOnlyList<MyFreezeItem> Freezes,
    IReadOnlyList<MyPassEntryItem> Entries);

/// <param name="EndsOn">Nulo = abierto (sigue congelada hasta que se reanude).</param>
/// <param name="State"><c>scheduled</c>, <c>active</c> o <c>ended</c>.</param>
/// <param name="DaysCredited">Días que corrieron el fin de la membresía.</param>
public sealed record MyFreezeItem(DateOnly StartsOn, DateOnly? EndsOn, string Reason, string State, int DaysCredited);

/// <summary>Un día en que el miembro gastó una entrada de su tiquetera.</summary>
public sealed record MyPassEntryItem(DateOnly UsedOn);
