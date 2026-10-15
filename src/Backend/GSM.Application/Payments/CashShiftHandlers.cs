using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Payments;

/// <summary>La barra de la caja: el turno abierto con su efectivo esperado y la base sugerida. Cualquier staff.</summary>
public sealed class GetCashDrawerStatusHandler(
    ICurrentUserService currentUser,
    ICashShiftRepository shifts,
    CashDrawerService drawer)
{
    public async Task<CashDrawerStatusResponse> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var open = await shifts.GetOpenAsync(cancellationToken);
        var lastClosed = open is null ? await shifts.GetLastClosedAsync(cancellationToken) : null;

        return new CashDrawerStatusResponse(
            open is null ? null : await drawer.ToResponseAsync(open, cancellationToken),
            lastClosed?.CountedCash ?? 0m);
    }
}

/// <summary>
/// Abre la caja del turno con una base en efectivo. Una sola abierta por gym: si hay otra, 409 (y si quedó
/// olvidada por alguien, el mensaje dice de quién).
/// </summary>
public sealed class OpenCashShiftHandler(
    ICurrentUserService currentUser,
    IClock clock,
    ICashShiftRepository shifts,
    CashDrawerService drawer,
    IUnitOfWork unitOfWork,
    ILogger<OpenCashShiftHandler> logger)
{
    public async Task<CashShiftResponse> HandleAsync(OpenCashShiftRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        if (await shifts.GetOpenAsync(cancellationToken) is { } open)
        {
            throw new ConflictException(open.OpenedBy == userId
                ? "Ya tienes la caja abierta."
                : await drawer.StaleMessageAsync(open, cancellationToken));
        }

        var shift = CashShift.Open(gymId, userId, clock.UtcNow, request.OpeningCash);
        shifts.Add(shift);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Caja {ShiftId} abierta en gym {GymId} con base {OpeningCash}", shift.Id, gymId, shift.OpeningCash);

        return await drawer.ToResponseAsync(shift, cancellationToken);
    }
}

/// <summary>Retiro o ingreso de efectivo a mitad de turno. Lo hace quien abrió la caja o el Owner.</summary>
public sealed class RecordCashMovementHandler(
    ICurrentUserService currentUser,
    IClock clock,
    ICashShiftRepository shifts,
    CashDrawerService drawer,
    IUnitOfWork unitOfWork,
    ILogger<RecordCashMovementHandler> logger)
{
    public async Task<CashMovementResponse> HandleAsync(CashMovementRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        var open = await shifts.GetOpenAsync(cancellationToken)
            ?? throw new ConflictException("No hay caja abierta: ábrela para registrar movimientos de efectivo.");

        if (open.OpenedBy != userId && !drawer.IsOwnerLike)
        {
            throw new ForbiddenDomainException("Solo quien abrió la caja o el Owner puede registrar movimientos.");
        }

        var type = request.Type?.Trim().ToLowerInvariant() switch
        {
            "withdrawal" => CashMovementType.Withdrawal,
            "deposit" => CashMovementType.Deposit,
            _ => throw new ValidationDomainException("El tipo debe ser withdrawal o deposit."),
        };

        var movement = CashMovement.Create(
            gymId, open.Id, type, request.Amount, request.Reason, userId, clock.UtcNow);
        shifts.Add(movement);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Movimiento de caja {MovementId} ({Type}) por {Amount} en el turno {ShiftId}",
            movement.Id, movement.Type, movement.Amount, open.Id);

        var names = await drawer.ResolveNamesAsync([userId], cancellationToken);
        return CashDrawerService.ToResponse(movement, names);
    }
}

/// <summary>
/// Cierra la caja con el arqueo: calcula y guarda el esperado, compara con lo contado y, si no cuadra, exige una
/// nota. Cierra quien abrió, el Owner o —si quedó olvidada— quien viene a abrir la suya (§4.1). Devuelve el reporte.
/// </summary>
public sealed class CloseCashShiftHandler(
    ICurrentUserService currentUser,
    IClock clock,
    ICashShiftRepository shifts,
    IVoidRequestRepository voidRequests,
    CashDrawerService drawer,
    IUnitOfWork unitOfWork,
    ILogger<CloseCashShiftHandler> logger)
{
    public async Task<CashShiftReportResponse> HandleAsync(CloseCashShiftRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        var open = await shifts.GetOpenAsync(cancellationToken)
            ?? throw new ConflictException("No hay caja abierta.");

        if (open.OpenedBy != userId && !drawer.IsOwnerLike && !drawer.IsStale(open))
        {
            throw new ForbiddenDomainException("Solo quien abrió la caja o el Owner puede cerrarla.");
        }

        var expected = await drawer.ComputeExpectedAsync(open, cancellationToken);
        open.Close(userId, clock.UtcNow, request.CountedCash, expected, request.Notes); // 422 si no cuadra sin nota
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Caja {ShiftId} cerrada: esperado {Expected}, contado {Counted}, diferencia {Difference}",
            open.Id, open.ExpectedCash, open.CountedCash, open.Difference);

        return await drawer.BuildReportAsync(open, voidRequests, cancellationToken);
    }
}

/// <summary>
/// Reporte de un turno. Lo ve quien lo abrió o lo cerró (lo que cobró en su turno) y el Owner o quien tiene
/// <c>ViewFinances</c> (los de otras personas).
/// </summary>
public sealed class GetCashShiftReportHandler(
    ICurrentUserService currentUser,
    ICashShiftRepository shifts,
    IVoidRequestRepository voidRequests,
    CashDrawerService drawer)
{
    public async Task<CashShiftReportResponse> HandleAsync(Guid shiftId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var userId = currentUser.UserId;

        var shift = await shifts.GetByIdAsync(shiftId, cancellationToken)
            ?? throw new NotFoundException("Turno no encontrado.");

        var own = shift.OpenedBy == userId || shift.ClosedBy == userId;
        if (!own && !currentUser.HasPermission(StaffPermission.ViewFinances))
        {
            throw new ForbiddenDomainException("No tienes permiso para ver el turno de otra persona.");
        }

        return await drawer.BuildReportAsync(shift, voidRequests, cancellationToken);
    }
}

/// <summary>Lista de turnos con sus diferencias, filtrable por persona y fecha. Owner o <c>ViewFinances</c>.</summary>
public sealed class ListCashShiftsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    ICashShiftRepository shifts,
    CashDrawerService drawer)
{
    public async Task<PagedResult<CashShiftResponse>> HandleAsync(
        ListCashShiftsQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        if (!currentUser.HasPermission(StaffPermission.ViewFinances))
        {
            throw new ForbiddenDomainException("No tienes permiso para ver los turnos de caja.");
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        DateTimeOffset? fromUtc = query.From is { } from ? clock.BusinessDayRange(from).Start : null;
        DateTimeOffset? toUtc = query.To is { } to ? clock.BusinessDayRange(to).End : null;

        var (items, total) = await shifts.GetPageAsync(fromUtc, toUtc, query.OpenedBy, page, pageSize, cancellationToken);

        var names = await drawer.ResolveNamesAsync(
            items.SelectMany(s => new[] { s.OpenedBy, s.ClosedBy ?? Guid.Empty }), cancellationToken);

        var rows = new List<CashShiftResponse>(items.Count);
        foreach (var shift in items)
        {
            var expected = shift.IsOpen ? await drawer.ComputeExpectedAsync(shift, cancellationToken) : shift.ExpectedCash;
            rows.Add(drawer.ToResponse(shift, names, expected));
        }

        return new PagedResult<CashShiftResponse>(rows, page, pageSize, total);
    }
}
