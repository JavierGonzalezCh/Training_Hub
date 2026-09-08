using FluentValidation;
using GSM.Application.Attendance.Contracts;

namespace GSM.Application.Attendance.Validators;

public sealed class CheckInRequestValidator : AbstractValidator<CheckInRequest>
{
    public CheckInRequestValidator()
    {
        RuleFor(x => x.QrCode).MaximumLength(64);
        RuleFor(x => x.Method).IsInEnum().When(x => x.Method is not null);
    }
}
