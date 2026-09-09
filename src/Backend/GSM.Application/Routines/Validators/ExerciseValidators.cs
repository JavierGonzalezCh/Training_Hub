using FluentValidation;
using GSM.Application.Routines.Contracts;

namespace GSM.Application.Routines.Validators;

public sealed class CreateExerciseRequestValidator : AbstractValidator<CreateExerciseRequest>
{
    public CreateExerciseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 120);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.VideoUrl).MaximumLength(2048);
        RuleFor(x => x.ImageUrl).MaximumLength(2048);
        RuleFor(x => x.PrimaryMuscle).IsInEnum();
        RuleForEach(x => x.SecondaryMuscles).IsInEnum();
        RuleForEach(x => x.Equipment).IsInEnum();
    }
}

public sealed class UpdateExerciseRequestValidator : AbstractValidator<UpdateExerciseRequest>
{
    public UpdateExerciseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 120);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.VideoUrl).MaximumLength(2048);
        RuleFor(x => x.ImageUrl).MaximumLength(2048);
        RuleFor(x => x.PrimaryMuscle).IsInEnum();
        RuleForEach(x => x.SecondaryMuscles).IsInEnum();
        RuleForEach(x => x.Equipment).IsInEnum();
    }
}
