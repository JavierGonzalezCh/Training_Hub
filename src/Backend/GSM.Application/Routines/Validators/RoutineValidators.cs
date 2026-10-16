using FluentValidation;
using GSM.Application.Routines.Contracts;

namespace GSM.Application.Routines.Validators;

public sealed class SaveRoutineRequestValidator : AbstractValidator<SaveRoutineRequest>
{
    public SaveRoutineRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 120);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Days).NotEmpty();
        RuleFor(x => x.ScheduleMode)
            .Must(m => m is null or "rotating" or "weekly")
            .WithMessage("El modo de calendario debe ser \"weekly\" o \"rotating\".");
        RuleForEach(x => x.Days).SetValidator(new RoutineDayInputValidator());
    }
}

public sealed class RoutineDayInputValidator : AbstractValidator<RoutineDayInput>
{
    public RoutineDayInputValidator()
    {
        RuleFor(x => x.Name).MaximumLength(60);
        RuleFor(x => x.DayOfWeek).InclusiveBetween(1, 7).When(x => x.DayOfWeek.HasValue);
        RuleForEach(x => x.Exercises).SetValidator(new RoutineExerciseInputValidator());
    }
}

public sealed class RoutineExerciseInputValidator : AbstractValidator<RoutineExerciseInput>
{
    public RoutineExerciseInputValidator()
    {
        RuleFor(x => x.ExerciseId).NotEmpty();
        RuleFor(x => x.SetTargets).NotEmpty().Must(t => t is null || t.Count <= 30)
            .WithMessage("Una línea admite como máximo 30 series.");
        RuleForEach(x => x.SetTargets).SetValidator(new SetTargetInputValidator());
        RuleFor(x => x.Group).GreaterThanOrEqualTo(1).When(x => x.Group.HasValue);
        RuleFor(x => x.RestSeconds).GreaterThanOrEqualTo(0).When(x => x.RestSeconds.HasValue);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class SetTargetInputValidator : AbstractValidator<SetTargetInput>
{
    public SetTargetInputValidator()
    {
        RuleFor(x => x.SetType).Must(t => GSM.Domain.Routines.SetTypes.Parse(t) is not null)
            .WithMessage("El tipo de serie debe ser \"normal\", \"warmup\", \"drop\" o \"failure\".");
    }
}

public sealed class UpdateRoutineSettingsRequestValidator : AbstractValidator<UpdateRoutineSettingsRequest>
{
    public UpdateRoutineSettingsRequestValidator()
    {
        RuleFor(x => x.ReviewWeeks).InclusiveBetween(1, 52);
    }
}
