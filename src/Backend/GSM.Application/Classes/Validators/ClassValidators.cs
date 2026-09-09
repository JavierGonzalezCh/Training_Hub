using FluentValidation;
using GSM.Application.Classes.Contracts;

namespace GSM.Application.Classes.Validators;

/// <summary>
/// Validación de forma (400). Las reglas de coherencia recurrente/puntual son invariantes de
/// dominio (422) y viven en <c>GymClass</c>.
/// </summary>
public sealed class SaveClassRequestValidator : AbstractValidator<SaveClassRequest>
{
    public SaveClassRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 120);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.InstructorName).MaximumLength(120);
        RuleFor(x => x.Capacity).GreaterThanOrEqualTo(1).When(x => x.Capacity.HasValue);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(5, 480);
    }
}
