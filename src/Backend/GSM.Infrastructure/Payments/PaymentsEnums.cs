using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Payments;

/// <summary>Enums nativos de Postgres del módulo Payments.</summary>
public sealed class PaymentsEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Payments.PaymentMethod>("payment_method", "payments", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Payments.CashMovementType>("cash_movement_type", "payments", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Payments.VoidRequestStatus>("void_request_status", "payments", SnakeCaseNameTranslator.Instance),
    ];
}
