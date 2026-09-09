using FluentValidation;
using GSM.Application.SuperAdmin.Contracts;

namespace GSM.Application.SuperAdmin.Validators;

public sealed class UpdateSubscriptionRequestValidator : AbstractValidator<UpdateSubscriptionRequest>
{
    public UpdateSubscriptionRequestValidator()
    {
        RuleFor(x => x.Plan).IsInEnum();
        RuleFor(x => x.MonthlyPrice).GreaterThanOrEqualTo(0);
    }
}

public sealed class RegisterSubscriptionPaymentRequestValidator
    : AbstractValidator<RegisterSubscriptionPaymentRequest>
{
    public RegisterSubscriptionPaymentRequestValidator()
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
