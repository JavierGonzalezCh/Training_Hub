using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Payments;

/// <summary>Enums nativos de Postgres del módulo Payments.</summary>
public sealed class PaymentsEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Payments.PaymentMethod>("payment_method", "payments", LowerCaseNameTranslator.Instance),
    ];
}
