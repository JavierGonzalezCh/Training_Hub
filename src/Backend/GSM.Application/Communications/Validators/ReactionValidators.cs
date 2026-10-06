using FluentValidation;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Communications;

namespace GSM.Application.Communications.Validators;

public sealed class ReactRequestValidator : AbstractValidator<ReactRequest>
{
    public ReactRequestValidator()
    {
        RuleFor(x => x.Kind).NotEmpty()
            .Must(k => Enum.TryParse<ReactionKind>(k, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            .WithMessage("Reacción inválida: Like, Celebrate o Strong.");
    }
}
