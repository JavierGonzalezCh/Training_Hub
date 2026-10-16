using FluentValidation;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Routines;

namespace GSM.Application.Routines.Validators;

public sealed class SaveRoutineTemplateRequestValidator : AbstractValidator<SaveRoutineTemplateRequest>
{
    public SaveRoutineTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, RoutineTemplate.MaxNameLength);
        RuleFor(x => x.Description).MaximumLength(RoutineTemplate.MaxDescriptionLength);
        RuleFor(x => x.Routine).NotNull().SetValidator(new SaveRoutineRequestValidator());
    }
}

public sealed class SaveRoutineAsTemplateRequestValidator : AbstractValidator<SaveRoutineAsTemplateRequest>
{
    public SaveRoutineAsTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, RoutineTemplate.MaxNameLength);
        RuleFor(x => x.Description).MaximumLength(RoutineTemplate.MaxDescriptionLength);
    }
}
