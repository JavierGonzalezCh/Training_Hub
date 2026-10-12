using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Identity.Public;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Application.Payments.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Payments;

/// <summary>
/// El staff sin el permiso <c>VoidPayments</c> pide anular un pago, con motivo. No toca el pago: queda una
/// solicitud pendiente que aprueba o rechaza el Owner o quien tenga el permiso (Payments v2 §6).
/// </summary>
public sealed class RequestVoidHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IPaymentRepository payments,
    IVoidRequestRepository requests,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<RequestVoidHandler> logger)
{
    public async Task<VoidRequestResponse> HandleAsync(
        Guid paymentId, RequestVoidRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var requestedBy = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        var payment = await payments.GetByIdAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException("Pago no encontrado.");

        if (payment.ReversesPaymentId is not null)
        {
            throw new ValidationDomainException("Una anulación no se puede anular.");
        }

        if (await payments.IsReversedAsync(payment.Id, cancellationToken))
        {
            throw new ConflictException("Este pago ya está anulado.");
        }

        if (await requests.GetPendingForPaymentAsync(payment.Id, cancellationToken) is not null)
        {
            throw new ConflictException("Este pago ya tiene una solicitud de anulación pendiente.");
        }

        var voidRequest = VoidRequest.Create(gymId, payment.Id, request.Reason, requestedBy, clock.UtcNow);
        requests.Add(voidRequest);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Solicitud de anulación {RequestId} del pago {PaymentId} en gym {GymId}", voidRequest.Id, payment.Id, gymId);

        await events.PublishAsync(
            new VoidRequested(
                gymId, voidRequest.Id, payment.Id, requestedBy, payment.MemberId,
                payment.Concept, payment.Amount, voidRequest.Reason),
            cancellationToken);

        return VoidRequestMapper.ToResponse(voidRequest, payment, new Dictionary<Guid, string>());
    }
}

/// <summary>Bandeja de solicitudes de anulación. Solo Owner o <c>VoidPayments</c>.</summary>
public sealed class ListVoidRequestsHandler(
    ICurrentUserService currentUser,
    IPaymentRepository payments,
    IVoidRequestRepository requests,
    IIdentityReader identity)
{
    public async Task<PagedResult<VoidRequestResponse>> HandleAsync(
        ListVoidRequestsQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        if (!currentUser.HasPermission(StaffPermission.VoidPayments))
        {
            throw new ForbiddenDomainException("No tienes permiso para ver las solicitudes de anulación.");
        }

        VoidRequestStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            status = query.Status.Trim().ToLowerInvariant() switch
            {
                "pending" => VoidRequestStatus.Pending,
                "approved" => VoidRequestStatus.Approved,
                "rejected" => VoidRequestStatus.Rejected,
                _ => throw new ValidationDomainException("El estado debe ser pending, approved o rejected."),
            };
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var (items, total) = await requests.GetPageAsync(status, page, pageSize, cancellationToken);

        var names = new Dictionary<Guid, string>();
        foreach (var id in items.SelectMany(r => new[] { r.RequestedBy, r.ResolvedBy ?? Guid.Empty })
                     .Where(id => id != Guid.Empty).Distinct())
        {
            if (await identity.GetUserAsync(id, cancellationToken) is { } user)
            {
                names[id] = user.FullName;
            }
        }

        var rows = new List<VoidRequestResponse>(items.Count);
        foreach (var request in items)
        {
            var payment = await payments.GetByIdAsync(request.PaymentId, cancellationToken);
            if (payment is not null)
            {
                rows.Add(VoidRequestMapper.ToResponse(request, payment, names));
            }
        }

        return new PagedResult<VoidRequestResponse>(rows, page, pageSize, total);
    }
}

/// <summary>
/// Aprueba una solicitud: crea el mismo asiento inverso que la anulación directa (fechado hoy, con el
/// motivo de quien pidió, firmado por quien aprueba) y cierra la solicitud en la misma transacción.
/// </summary>
public sealed class ApproveVoidRequestHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IPaymentRepository payments,
    IVoidRequestRepository requests,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<ApproveVoidRequestHandler> logger)
{
    public async Task<PaymentResponse> HandleAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var approver = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        var request = await requests.GetByIdAsync(requestId, cancellationToken)
            ?? throw new NotFoundException("Solicitud no encontrada.");
        var original = await payments.GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new NotFoundException("Pago no encontrado.");

        var now = clock.UtcNow;
        request.Approve(approver, now); // 409 si ya estaba resuelta

        if (await payments.IsReversedAsync(original.Id, cancellationToken))
        {
            throw new ConflictException("Este pago ya está anulado.");
        }

        var reversal = Payment.CreateReversal(original, request.Reason, approver, now);
        payments.Add(reversal);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Solicitud {RequestId} aprobada en gym {GymId}: pago {PaymentId} anulado con el asiento {ReversalId}",
            request.Id, gymId, original.Id, reversal.Id);

        await events.PublishAsync(
            new PaymentRecorded(reversal.Id, gymId, reversal.MemberId, reversal.MembershipId, reversal.Amount),
            cancellationToken);
        await events.PublishAsync(
            new VoidRequestResolved(gymId, request.Id, original.Id, approver, Approved: true), cancellationToken);

        return PaymentMapper.ToResponse(reversal);
    }
}

/// <summary>Rechaza una solicitud: la cierra sin tocar el pago.</summary>
public sealed class RejectVoidRequestHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IVoidRequestRepository requests,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<RejectVoidRequestHandler> logger)
{
    public async Task HandleAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        var request = await requests.GetByIdAsync(requestId, cancellationToken)
            ?? throw new NotFoundException("Solicitud no encontrada.");

        request.Reject(userId, clock.UtcNow); // 409 si ya estaba resuelta
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Solicitud {RequestId} rechazada en gym {GymId}", request.Id, gymId);
        await events.PublishAsync(
            new VoidRequestResolved(gymId, request.Id, request.PaymentId, userId, Approved: false), cancellationToken);
    }
}

internal static class VoidRequestMapper
{
    public static VoidRequestResponse ToResponse(
        VoidRequest r, Payment payment, IReadOnlyDictionary<Guid, string> names) => new(
        r.Id, r.PaymentId, r.Reason, r.RequestedBy,
        names.GetValueOrDefault(r.RequestedBy), r.RequestedAt,
        r.Status.ToString().ToLowerInvariant(),
        r.ResolvedBy, r.ResolvedBy is { } by ? names.GetValueOrDefault(by) : null, r.ResolvedAt,
        payment.Concept, payment.Amount, payment.Method.ToString(), payment.MemberId, payment.PaidAt);
}
