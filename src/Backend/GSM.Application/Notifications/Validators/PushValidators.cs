using FluentValidation;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications.Validators;

public sealed class RegisterPushDeviceRequestValidator : AbstractValidator<RegisterPushDeviceRequest>
{
    public RegisterPushDeviceRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(PushDevice.TokenMaxLength);
        RuleFor(x => x.Platform).NotEmpty()
            .Must(p => PushHandlers.TryParsePlatform(p, out _))
            .WithMessage("Plataforma inválida: android, web_member o web_panel.");
    }
}

public sealed class UnregisterPushDeviceRequestValidator : AbstractValidator<UnregisterPushDeviceRequest>
{
    public UnregisterPushDeviceRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(PushDevice.TokenMaxLength);
    }
}

public sealed class UpdateStaffPushPreferencesRequestValidator : AbstractValidator<UpdateStaffPushPreferencesRequest>
{
    public UpdateStaffPushPreferencesRequestValidator()
    {
        RuleFor(x => x.Items).NotNull();
        RuleForEach(x => x.Items).ChildRules(item =>
            item.RuleFor(i => i.Type)
                .Must(t => Enum.TryParse<NotificationType>(t, ignoreCase: true, out var parsed)
                    && StaffNotificationPreference.IsConfigurable(parsed))
                .WithMessage("Tipo de aviso inválido."));
    }
}
