using GSM.Application.Payments.Contracts;
using GSM.Domain.Payments;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Payments;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class PaymentMapper
{
    public static partial PaymentResponse ToResponse(Payment payment);
}
