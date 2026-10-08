using FluentValidation;
using GSM.Application.Classes.Contracts;

namespace GSM.Application.Classes.Validators;

public sealed class MarkAttendanceRequestValidator : AbstractValidator<MarkAttendanceRequest>
{
    public MarkAttendanceRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => (x.Attended?.Count ?? 0) + (x.NoShow?.Count ?? 0) > 0)
            .WithMessage("Indica al menos un miembro en 'attended' o en 'noShow'.");

        RuleForEach(x => x.Attended).NotEmpty();
        RuleForEach(x => x.NoShow).NotEmpty();
    }
}
