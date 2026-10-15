using FluentValidation;
using GSM.Application.Attendance.Contracts;

namespace GSM.Application.Attendance.Validators;

public sealed class CheckInRequestValidator : AbstractValidator<CheckInRequest>
{
    public CheckInRequestValidator()
    {
        RuleFor(x => x.GymCode).MaximumLength(64);
        RuleFor(x => x.AuthorizationReason).MaximumLength(500);
    }
}

public sealed class VoidCheckInRequestValidator : AbstractValidator<VoidCheckInRequest>
{
    public VoidCheckInRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(500);
    }
}

public sealed class SetEntryRuleRequestValidator : AbstractValidator<SetEntryRuleRequest>
{
    public SetEntryRuleRequestValidator()
    {
        RuleFor(x => x.MaxDailyEntries).InclusiveBetween(2, 5);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(500);
    }
}

public sealed class UpdateCheckinSettingsRequestValidator : AbstractValidator<UpdateCheckinSettingsRequest>
{
    public UpdateCheckinSettingsRequestValidator()
    {
        RuleFor(x => x.Mode)
            .Must(m => Enum.TryParse<GSM.Domain.Attendance.CheckinMode>(m, true, out var v) && Enum.IsDefined(v))
            .WithMessage("El modo debe ser Rotating o Static.");
    }
}
