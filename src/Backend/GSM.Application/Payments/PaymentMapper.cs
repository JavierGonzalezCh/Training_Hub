using GSM.Application.Payments.Contracts;
using GSM.Domain.Payments;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Payments;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class PaymentMapper
{
    [MapperIgnoreTarget(nameof(PaymentResponse.IsReversed))]
    public static partial PaymentResponse ToResponse(Payment payment);

    public static PaymentResponse ToResponse(Payment payment, IReadOnlySet<Guid> reversedIds)
        => ToResponse(payment) with { IsReversed = reversedIds.Contains(payment.Id) };
}
