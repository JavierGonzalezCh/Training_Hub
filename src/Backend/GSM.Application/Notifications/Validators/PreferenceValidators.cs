using FluentValidation;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications.Validators;

/// <summary>Forma del request (400): la lista existe y cada categoría es una de las configurables.</summary>
public sealed class UpdateNotificationPreferencesRequestValidator : AbstractValidator<UpdateNotificationPreferencesRequest>
{
    public UpdateNotificationPreferencesRequestValidator()
    {
        RuleFor(x => x.Items).NotNull();
        RuleForEach(x => x.Items).ChildRules(item =>
            item.RuleFor(i => i.Category)
                .Must(c => Enum.TryParse<NotificationCategory>(c, ignoreCase: true, out var parsed)
                    && MemberNotificationPreference.IsConfigurable(parsed))
                .WithMessage("Categoría inválida: Membership, Classes, Progress, News o Promotions."));
    }
}
