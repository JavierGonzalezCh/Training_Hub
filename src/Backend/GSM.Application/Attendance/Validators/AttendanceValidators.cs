using FluentValidation;
using GSM.Application.Attendance.Contracts;

namespace GSM.Application.Attendance.Validators;

public sealed class CheckInRequestValidator : AbstractValidator<CheckInRequest>
{
    public CheckInRequestValidator()
    {
        RuleFor(x => x.GymCode).MaximumLength(64);
    }
}

public sealed class UpdateCheckinSettingsRequestValidator : AbstractValidator<UpdateCheckinSettingsRequest>
{
    public UpdateCheckinSettingsRequestValidator()
    {
        RuleFor(x => x.Mode)
            .Must(m => Enum.TryParse<GSM.Domain.Attendance.CheckinMode>(m, true, out var v) && Enum.IsDefined(v))
            .WithMessage("El modo debe ser Rotating o Static.");
        RuleFor(x => x.GraceDays).InclusiveBetween(0, GSM.Domain.Attendance.CheckinSettings.MaxGraceDays);
    }
}
