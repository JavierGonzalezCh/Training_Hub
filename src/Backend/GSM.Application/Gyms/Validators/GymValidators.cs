using FluentValidation;
using GSM.Application.Gyms.Contracts;

namespace GSM.Application.Gyms.Validators;

internal static class GymRules
{
    public const string SlugPattern = "^[a-z0-9]+(-[a-z0-9]+)*$";
    public const string HexColorPattern = "^#(?i)[0-9a-f]{6}$";
}

public sealed class CreateGymRequestValidator : AbstractValidator<CreateGymRequest>
{
    public CreateGymRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Slug).NotEmpty().Length(2, 60).Matches(GymRules.SlugPattern);
        RuleFor(x => x.Nit).MaximumLength(20);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Plan).IsInEnum();

        RuleFor(x => x.Owner).NotNull();
        RuleFor(x => x.Owner.Email).NotEmpty().EmailAddress().MaximumLength(256)
            .When(x => x.Owner is not null);
        RuleFor(x => x.Owner.FullName).NotEmpty().MaximumLength(200)
            .When(x => x.Owner is not null);
        RuleFor(x => x.Owner.Password).NotEmpty().MinimumLength(10).MaximumLength(256)
            .When(x => x.Owner is not null);
    }
}

public sealed class UpdateGymRequestValidator : AbstractValidator<UpdateGymRequest>
{
    public UpdateGymRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Nit).MaximumLength(20);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.LogoUrl).MaximumLength(2048);
        RuleFor(x => x.PrimaryColor).NotEmpty().Matches(GymRules.HexColorPattern);
        RuleFor(x => x.SecondaryColor).NotEmpty().Matches(GymRules.HexColorPattern);
    }
}

public sealed class UpdateFeatureFlagsRequestValidator : AbstractValidator<UpdateFeatureFlagsRequest>
{
    // Todos los campos son bool no anulables; el binder ya garantiza forma. Sin reglas extra.
}
