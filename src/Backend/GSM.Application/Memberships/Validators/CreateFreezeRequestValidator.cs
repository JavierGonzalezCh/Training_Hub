using FluentValidation;
using GSM.Application.Memberships.Contracts;

namespace GSM.Application.Memberships.Validators;

public sealed class CreateFreezeRequestValidator : AbstractValidator<CreateFreezeRequest>
{
    public CreateFreezeRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(500);
    }
}
