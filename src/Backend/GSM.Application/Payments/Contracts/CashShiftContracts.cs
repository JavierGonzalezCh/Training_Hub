namespace GSM.Application.Payments.Contracts;

/// <summary>Si el negocio lleva el efectivo por turnos de caja. Apagado: sin turnos ni barra de caja.</summary>
public sealed record PaymentSettingsResponse(bool UseCashDrawer);

public sealed record UpdatePaymentSettingsRequest(bool UseCashDrawer);

public sealed record OpenCashShiftRequest(decimal OpeningCash);

public sealed record CloseCashShiftRequest(decimal CountedCash, string? Notes);

/// <summary><c>Type</c> es <c>withdrawal</c> (retiro) o <c>deposit</c> (ingreso), como texto para no depender del enum en el cliente.</summary>
public sealed record CashMovementRequest(string Type, decimal Amount, string Reason);

/// <summary>
/// Un turno de caja. Mientras está abierto, <c>ExpectedCash</c> es el esperado de ahora mismo; al cerrar es el
/// que se guardó. <c>IsStale</c>: abierto hace más de 16 horas (resaltado para el Owner).
/// </summary>
public sealed record CashShiftResponse(
    Guid Id,
    Guid OpenedBy,
    string? OpenedByName,
    DateTimeOffset OpenedAt,
    decimal OpeningCash,
    Guid? ClosedBy,
    string? ClosedByName,
    DateTimeOffset? ClosedAt,
    decimal? CountedCash,
    decimal? ExpectedCash,
    decimal? Difference,
    string? CloseNotes,
    bool IsOpen,
    bool IsStale);

/// <summary>La barra de la caja: el turno abierto (o <c>null</c>) y la base sugerida para abrir el siguiente.</summary>
public sealed record CashDrawerStatusResponse(CashShiftResponse? Shift, decimal SuggestedOpeningCash);

/// <summary><c>Type</c> es <c>withdrawal</c> o <c>deposit</c> (texto: NSwag infiere mal los enums de respuestas).</summary>
public sealed record CashMovementResponse(
    Guid Id,
    Guid ShiftId,
    string Type,
    decimal Amount,
    string Reason,
    Guid CreatedBy,
    string? CreatedByName,
    DateTimeOffset OccurredAt);

public sealed record CashMethodSummary(string Method, decimal Total, int Count);

/// <summary>
/// Reporte imprimible del turno (§4.4): base, cobros por método, movimientos, anulaciones, esperado, contado y
/// diferencia. <c>CashIn</c> = efectivo cobrado; <c>CashOut</c> = devoluciones y anulaciones en efectivo que
/// salieron de la caja (magnitud positiva).
/// </summary>
public sealed record CashShiftReportResponse(
    CashShiftResponse Shift,
    decimal CashIn,
    decimal CashOut,
    decimal Deposits,
    decimal Withdrawals,
    IReadOnlyList<CashMethodSummary> Methods,
    IReadOnlyList<CashMovementResponse> Movements,
    IReadOnlyList<PaymentResponse> Payments,
    IReadOnlyList<PaymentResponse> Reversals);

public sealed record ListCashShiftsQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? OpenedBy { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
