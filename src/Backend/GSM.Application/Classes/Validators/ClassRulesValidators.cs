using FluentValidation;
using GSM.Application.Classes.Contracts;
using GSM.Domain.Classes;

namespace GSM.Application.Classes.Validators;

public sealed class UpdateClassSettingsRequestValidator : AbstractValidator<UpdateClassSettingsRequest>
{
    public UpdateClassSettingsRequestValidator()
    {
        RuleFor(x => x.CancelDeadlineHours).InclusiveBetween(0, ClassSettings.MaxCancelDeadlineHours);
        RuleFor(x => x.NoShowLimit).InclusiveBetween(1, ClassSettings.MaxNoShowLimit);
        RuleFor(x => x.NoShowWindowDays).InclusiveBetween(1, ClassSettings.MaxNoShowWindowDays);
        RuleFor(x => x.NoShowBlockDays).InclusiveBetween(1, ClassSettings.MaxNoShowBlockDays);
        RuleFor(x => x.BookingWindowDays).InclusiveBetween(1, ClassSettings.MaxBookingWindowDays);
        RuleFor(x => x.MaxActiveBookings).InclusiveBetween(1, ClassSettings.MaxActiveBookingsLimit);
        RuleFor(x => x.WaitlistOfferMinutes)
            .InclusiveBetween(ClassSettings.MinWaitlistOfferMinutes, ClassSettings.MaxWaitlistOfferMinutes)
            .When(x => x.WaitlistOfferMinutes is not null);
    }
}

public sealed class LiftBookingBlockRequestValidator : AbstractValidator<LiftBookingBlockRequest>
{
    public LiftBookingBlockRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
