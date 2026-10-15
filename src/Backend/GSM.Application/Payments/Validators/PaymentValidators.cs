using FluentValidation;
using GSM.Application.Payments.Contracts;

namespace GSM.Application.Payments.Validators;

public sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.Amount).NotEqual(0m);
        RuleFor(x => x.Amount).GreaterThan(-100_000_000m).LessThan(100_000_000m);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Concept).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.MemberId)
            .NotNull()
            .When(x => x.MembershipId is not null)
            .WithMessage("Un pago con membershipId debe indicar también memberId.");
    }
}

public sealed class OpenCashShiftRequestValidator : AbstractValidator<OpenCashShiftRequest>
{
    public OpenCashShiftRequestValidator()
    {
        RuleFor(x => x.OpeningCash).GreaterThanOrEqualTo(0m).LessThan(100_000_000m);
    }
}

public sealed class CloseCashShiftRequestValidator : AbstractValidator<CloseCashShiftRequest>
{
    public CloseCashShiftRequestValidator()
    {
        RuleFor(x => x.CountedCash).GreaterThanOrEqualTo(0m).LessThan(100_000_000m);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CashMovementRequestValidator : AbstractValidator<CashMovementRequest>
{
    public CashMovementRequestValidator()
    {
        RuleFor(x => x.Type).Must(t => t is "withdrawal" or "deposit")
            .WithMessage("El tipo debe ser withdrawal o deposit.");
        RuleFor(x => x.Amount).GreaterThan(0m).LessThan(100_000_000m);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(300);
    }
}

public sealed class VoidPaymentRequestValidator : AbstractValidator<VoidPaymentRequest>
{
    public VoidPaymentRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(500);
    }
}
