using FluentValidation;
using GSM.Application.Identity.Contracts;

namespace GSM.Application.Identity.Validators;

public sealed class CreateKioskRequestValidator : AbstractValidator<CreateKioskRequest>
{
    public CreateKioskRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class PairKioskRequestValidator : AbstractValidator<PairKioskRequest>
{
    public PairKioskRequestValidator()
    {
        RuleFor(x => x.PairingCode).NotEmpty().MaximumLength(20);
    }
}

public sealed class KioskRefreshRequestValidator : AbstractValidator<KioskRefreshRequest>
{
    public KioskRefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
    }
}
