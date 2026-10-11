using FluentValidation;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Communications;

namespace GSM.Application.Communications.Validators;

/// <summary>Forma del request (400). El mismo límite vive en <see cref="WhatsAppTemplate"/> (422).</summary>
public sealed class UpdateWhatsAppTemplateRequestValidator : AbstractValidator<UpdateWhatsAppTemplateRequest>
{
    public UpdateWhatsAppTemplateRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(WhatsAppTemplateDefaults.MaxLength);
    }
}

public sealed class RegisterWhatsAppContactRequestValidator : AbstractValidator<RegisterWhatsAppContactRequest>
{
    public RegisterWhatsAppContactRequestValidator()
    {
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.Kind).NotEmpty().Must(WhatsAppKinds.IsValid).WithMessage("Tipo de plantilla desconocido.");
    }
}
