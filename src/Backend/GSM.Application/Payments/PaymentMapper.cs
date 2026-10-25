using GSM.Application.Payments.Contracts;
using GSM.Domain.Payments;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Payments;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class PaymentMapper
{
    [MapperIgnoreTarget(nameof(PaymentResponse.IsReversed))]
    [MapperIgnoreTarget(nameof(PaymentResponse.HasPendingVoidRequest))]
    [MapperIgnoreTarget(nameof(PaymentResponse.RegisteredByName))]
    [MapperIgnoreTarget(nameof(PaymentResponse.MemberName))]
    public static partial PaymentResponse ToResponse(Payment payment);

    public static PaymentResponse ToResponse(
        Payment payment, IReadOnlySet<Guid> reversedIds, IReadOnlySet<Guid>? pendingVoidIds = null)
        => ToResponse(payment) with
        {
            IsReversed = reversedIds.Contains(payment.Id),
            HasPendingVoidRequest = pendingVoidIds?.Contains(payment.Id) ?? false,
        };
}
