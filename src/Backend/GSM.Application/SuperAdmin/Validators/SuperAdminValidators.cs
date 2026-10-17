using FluentValidation;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin.Validators;

public sealed class UpdateSubscriptionRequestValidator : AbstractValidator<UpdateSubscriptionRequest>
{
    public UpdateSubscriptionRequestValidator()
    {
        RuleFor(x => x.MonthlyPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MemberLimitOverride).GreaterThanOrEqualTo(1).When(x => x.MemberLimitOverride.HasValue);
        RuleFor(x => x.TrialGraceDays).InclusiveBetween(0, GymSubscription.MaxTrialGraceDays)
            .When(x => x.TrialGraceDays.HasValue);
    }
}

public sealed class CreateSaasPlanRequestValidator : AbstractValidator<CreateSaasPlanRequest>
{
    public CreateSaasPlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.MemberLimit).GreaterThanOrEqualTo(1).When(x => x.MemberLimit.HasValue);
        RuleFor(x => x.SuggestedPrice).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateSaasPlanRequestValidator : AbstractValidator<UpdateSaasPlanRequest>
{
    public UpdateSaasPlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.MemberLimit).GreaterThanOrEqualTo(1).When(x => x.MemberLimit.HasValue);
        RuleFor(x => x.SuggestedPrice).GreaterThanOrEqualTo(0);
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
