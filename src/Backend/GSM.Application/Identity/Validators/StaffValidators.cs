using FluentValidation;
using GSM.Application.Identity.Contracts;

namespace GSM.Application.Identity.Validators;

public sealed class CreateStaffRequestValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Profile)
            .Must(p => PermissionName.TryParseProfile(p, out _))
            .When(x => x.Profile is not null)
            .WithMessage("Perfil desconocido. Usa reception, trainer o administrator.");
    }
}

public sealed class UpdateStaffPermissionsRequestValidator : AbstractValidator<UpdateStaffPermissionsRequest>
{
    public UpdateStaffPermissionsRequestValidator()
    {
        RuleFor(x => x.Profile)
            .NotEmpty()
            .Must(p => PermissionName.TryParseProfile(p, out _))
            .WithMessage("Perfil desconocido. Usa reception, trainer o administrator.");
        RuleFor(x => x.Permissions).NotNull();
        RuleForEach(x => x.Permissions)
            .Must(p => PermissionName.TryParse(p, out _))
            .WithMessage("Permiso desconocido: '{PropertyValue}'.");
    }
}
