using FluentValidation;
using GSM.Application.Memberships.Contracts;

namespace GSM.Application.Memberships.Validators;

public sealed class CreatePlanRequestValidator : AbstractValidator<CreatePlanRequest>
{
    public CreatePlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DurationDays).InclusiveBetween(1, 3650);
    }
}

public sealed class UpdatePlanRequestValidator : AbstractValidator<UpdatePlanRequest>
{
    public UpdatePlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DurationDays).InclusiveBetween(1, 3650);
    }
}

public sealed class AssignMembershipRequestValidator : AbstractValidator<AssignMembershipRequest>
{
    public AssignMembershipRequestValidator()
        => RuleFor(x => x.PlanId).NotEmpty();
}

public sealed class CancelMembershipRequestValidator : AbstractValidator<CancelMembershipRequest>
{
    public CancelMembershipRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(500);
    }
}
