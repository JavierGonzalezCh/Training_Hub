using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Domain.Payments;

namespace GSM.Application.Payments;

/// <summary>
/// Reglas de la caja por turno que comparten varios casos de uso (Payments v2 §4): qué turno está abierto, si quedó
/// olvidado, cuánto efectivo se espera, a qué turno va un asiento y cómo se arman las respuestas.
/// </summary>
public sealed class CashDrawerService(
    ICurrentUserService currentUser,
    IClock clock,
    ICashShiftRepository shifts,
    IPaymentRepository payments,
    IIdentityReader identity)
{
    /// <summary>Una caja abierta hace más de estas horas está "olvidada" (§4.1).</summary>
    public const int StaleAfterHours = 16;

    /// <summary>El Owner (o un SuperAdmin impersonando) puede operar cualquier caja.</summary>
    public bool IsOwnerLike => currentUser.Role == UserRole.Owner || currentUser.IsImpersonating;

    public Task<CashShift?> GetOpenAsync(CancellationToken cancellationToken)
        => shifts.GetOpenAsync(cancellationToken);

    public bool IsStale(CashShift shift)
        => shift.IsOpen && clock.UtcNow - shift.OpenedAt > TimeSpan.FromHours(StaleAfterHours);

    /// <summary>
    /// La caja abierta, lista para recibir efectivo. 409 si no hay ninguna ("Abre la caja…") o si quedó olvidada
    /// por otra persona (debe cerrarla con su conteo antes de abrir la suya).
    /// </summary>
    public async Task<CashShift> RequireOpenForCashAsync(CancellationToken cancellationToken)
    {
        var open = await shifts.GetOpenAsync(cancellationToken)
            ?? throw new ConflictException("Abre la caja para cobrar en efectivo.");

        if (IsStale(open) && open.OpenedBy != currentUser.UserId && !IsOwnerLike)
        {
            throw new ConflictException(await StaleMessageAsync(open, cancellationToken));
        }

        return open;
    }

    public async Task<string> StaleMessageAsync(CashShift open, CancellationToken cancellationToken)
    {
        var name = (await ResolveNamesAsync([open.OpenedBy], cancellationToken)).GetValueOrDefault(open.OpenedBy)
            ?? "otra persona";
        var since = clock.ToBusinessLocal(open.OpenedAt).ToString("dd/MM HH:mm");
        return $"La caja de {name} sigue abierta desde {since}. Ciérrala con tu conteo antes de abrir la tuya.";
    }

    /// <summary>
    /// Turno y efecto en la caja del asiento inverso de <paramref name="original"/> (§4.3). Un cobro no en
    /// efectivo nunca mueve el cajón. Uno en efectivo del turno abierto entra en esa misma caja. Uno de un turno ya
    /// cerrado exige <paramref name="refundNow"/>: sí, sale de la caja abierta; no, el dinero nunca estuvo.
    /// </summary>
    public async Task<(Guid? ShiftId, bool AffectsCashDrawer)> PlanReversalAsync(
        Payment original, bool? refundNow, CancellationToken cancellationToken)
    {
        var open = await shifts.GetOpenAsync(cancellationToken);

        if (!original.Method.GoesToCashDrawer())
        {
            return (open?.Id, false);
        }

        if (open is not null && original.ShiftId == open.Id)
        {
            return (open.Id, true);
        }

        if (refundNow is null)
        {
            throw new ValidationDomainException(
                "Este pago en efectivo es de un turno ya cerrado: indica si el dinero se devuelve ahora.");
        }

        if (refundNow == true)
        {
            var shift = await RequireOpenForCashAsync(cancellationToken);
            return (shift.Id, true);
        }

        return (open?.Id, false);
    }

    /// <summary>¿Anular este pago exige decidir si el dinero se devuelve ahora? (efectivo de un turno ya cerrado).</summary>
    public async Task<bool> RequiresRefundDecisionAsync(Payment original, CancellationToken cancellationToken)
    {
        if (!original.Method.GoesToCashDrawer() || original.ReversesPaymentId is not null)
        {
            return false;
        }

        var open = await shifts.GetOpenAsync(cancellationToken);
        return open is null || original.ShiftId != open.Id;
    }

    /// <summary>Efectivo esperado: base + cobros en efectivo − devoluciones/anulaciones + ingresos − retiros.</summary>
    public async Task<decimal> ComputeExpectedAsync(CashShift shift, CancellationToken cancellationToken)
    {
        var (cashIn, cashOut) = await payments.GetCashDrawerTotalsAsync(shift.Id, cancellationToken);
        var (deposits, withdrawals) = await shifts.GetMovementTotalsAsync(shift.Id, cancellationToken);
        return shift.OpeningCash + cashIn - cashOut + deposits - withdrawals;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ResolveNamesAsync(
        IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in userIds.Where(id => id != Guid.Empty).Distinct())
        {
            if (await identity.GetUserAsync(id, cancellationToken) is { } user)
            {
                names[id] = user.FullName;
            }
        }

        return names;
    }

    /// <summary>La respuesta del turno. Abierto: el esperado se calcula ahora; cerrado: el guardado.</summary>
    public async Task<CashShiftResponse> ToResponseAsync(CashShift shift, CancellationToken cancellationToken)
    {
        var names = await ResolveNamesAsync(
            [shift.OpenedBy, shift.ClosedBy ?? Guid.Empty], cancellationToken);
        var expected = shift.IsOpen ? await ComputeExpectedAsync(shift, cancellationToken) : shift.ExpectedCash;
        return ToResponse(shift, names, expected);
    }

    public CashShiftResponse ToResponse(CashShift shift, IReadOnlyDictionary<Guid, string> names, decimal? expected)
        => new(
            shift.Id,
            shift.OpenedBy,
            names.GetValueOrDefault(shift.OpenedBy),
            shift.OpenedAt,
            shift.OpeningCash,
            shift.ClosedBy,
            shift.ClosedBy is { } by ? names.GetValueOrDefault(by) : null,
            shift.ClosedAt,
            shift.CountedCash,
            expected,
            shift.Difference,
            shift.CloseNotes,
            shift.IsOpen,
            IsStale(shift));

    public static CashMovementResponse ToResponse(CashMovement m, IReadOnlyDictionary<Guid, string> names)
        => new(
            m.Id,
            m.ShiftId,
            m.Type == CashMovementType.Withdrawal ? "withdrawal" : "deposit",
            m.Amount,
            m.Reason,
            m.CreatedBy,
            names.GetValueOrDefault(m.CreatedBy),
            m.OccurredAt);

    public static IReadOnlyList<CashMethodSummary> SummarizeByMethod(IEnumerable<Payment> items)
        => items
            .GroupBy(p => p.Method)
            .OrderBy(g => g.Key)
            .Select(g => new CashMethodSummary(g.Key.ToString(), g.Sum(p => p.Amount), g.Count()))
            .ToList();

    /// <summary>Pagos con el nombre de quien cobró y las marcas de anulación resueltas.</summary>
    public async Task<IReadOnlyList<PaymentResponse>> ToPaymentResponsesAsync(
        IReadOnlyList<Payment> items,
        IReadOnlySet<Guid> reversedIds,
        IReadOnlySet<Guid>? pendingVoidIds,
        CancellationToken cancellationToken)
    {
        var names = await ResolveNamesAsync(items.Select(p => p.RegisteredBy), cancellationToken);
        return items
            .Select(p => PaymentMapper.ToResponse(p, reversedIds, pendingVoidIds)
                with { RegisteredByName = names.GetValueOrDefault(p.RegisteredBy) })
            .ToList();
    }

    /// <summary>Arma el reporte de un turno (§4.4).</summary>
    public async Task<CashShiftReportResponse> BuildReportAsync(
        CashShift shift, IVoidRequestRepository voidRequests, CancellationToken cancellationToken)
    {
        var shiftPayments = await payments.GetByShiftAsync(shift.Id, cancellationToken);
        var ids = shiftPayments.Select(p => p.Id).ToList();
        var reversed = await payments.GetReversedIdsAsync(ids, cancellationToken);
        var pending = await voidRequests.GetPendingPaymentIdsAsync(ids, cancellationToken);

        var movements = await shifts.GetMovementsAsync([shift.Id], cancellationToken);
        var names = await ResolveNamesAsync(
            movements.Select(m => m.CreatedBy).Append(shift.OpenedBy).Append(shift.ClosedBy ?? Guid.Empty),
            cancellationToken);

        var (cashIn, cashOut) = await payments.GetCashDrawerTotalsAsync(shift.Id, cancellationToken);
        var (deposits, withdrawals) = await shifts.GetMovementTotalsAsync(shift.Id, cancellationToken);
        var expected = shift.IsOpen ? shift.OpeningCash + cashIn - cashOut + deposits - withdrawals : shift.ExpectedCash;

        var responses = await ToPaymentResponsesAsync(shiftPayments, reversed, pending, cancellationToken);

        return new CashShiftReportResponse(
            ToResponse(shift, names, expected),
            cashIn,
            cashOut,
            deposits,
            withdrawals,
            SummarizeByMethod(shiftPayments),
            movements.Select(m => ToResponse(m, names)).ToList(),
            responses,
            responses.Where(r => r.ReversesPaymentId is not null).ToList());
    }
}
