using FluentValidation;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Communications;

namespace GSM.Application.Communications.Validators;

/// <summary>Forma del request (400). El mismo rango vive en <see cref="CommunicationsSettings"/> (422).</summary>
public sealed class UpdateCommunicationsSettingsRequestValidator : AbstractValidator<UpdateCommunicationsSettingsRequest>
{
    public UpdateCommunicationsSettingsRequestValidator()
    {
        RuleFor(x => x.ReminderDaysBefore).InclusiveBetween(0, CommunicationsSettings.MaxReminderDaysBefore);
    }
}

public sealed class UpdateBirthdaySettingsRequestValidator : AbstractValidator<UpdateBirthdaySettingsRequest>
{
    public UpdateBirthdaySettingsRequestValidator()
    {
        RuleFor(x => x.Message).MaximumLength(CommunicationsSettings.BirthdayMessageMaxLength);
    }
}
