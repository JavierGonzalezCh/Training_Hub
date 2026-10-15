using FluentValidation;
using GSM.Application.Identity.Contracts;

namespace GSM.Application.Identity.Validators;

public sealed class InviteOwnerRequestValidator : AbstractValidator<InviteOwnerRequest>
{
    public InviteOwnerRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
    }
}
