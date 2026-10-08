using FluentValidation;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Members;

namespace GSM.Application.Members.Validators;

/// <summary>
/// Reglas de FORMA de la ficha (400): longitudes, formato de email, enums reconocibles. Las reglas de
/// negocio (menor de edad sin acudiente, teléfono ininterpretable, referente de otro gym) son del
/// dominio y de los casos de uso: llegan como 422.
/// </summary>
internal static class MemberProfileRules
{
    public static void Apply<T>(AbstractValidator<T> v, IClock clock) where T : IMemberProfileInput
    {
        v.RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        v.RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        v.RuleFor(x => x.DocumentType).NotEmpty().Must(IsEnum<DocumentType>).WithMessage("Tipo de documento no válido.");
        v.RuleFor(x => x.DocumentNumber).NotEmpty().MaximumLength(30);
        v.RuleFor(x => x.BirthDate)
            .Must(d => d is null || d < clock.Today)
            .WithMessage("La fecha de nacimiento debe ser pasada.");
        v.RuleFor(x => x.Gender).Must(IsEnum<Gender>).WithMessage("Género no válido.");
        v.RuleFor(x => x.PhotoUrl).MaximumLength(2048);
        v.RuleFor(x => x.Phone).MaximumLength(30);
        v.RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        v.RuleFor(x => x.Address).MaximumLength(200);
        v.RuleFor(x => x.City).MaximumLength(100);
        v.RuleFor(x => x.EmergencyContactName).MaximumLength(150);
        v.RuleFor(x => x.EmergencyContactRelationship).MaximumLength(50);
        v.RuleFor(x => x.EmergencyContactPhone).MaximumLength(30);
        v.RuleFor(x => x.Source).Must(IsEnum<MemberSource>).WithMessage("Origen no válido.");
        v.RuleFor(x => x.Goal).Must(IsEnum<MemberGoal>).WithMessage("Objetivo no válido.");
        v.RuleFor(x => x.Tags).Must(t => t is null || t.Count <= 50).WithMessage("Demasiadas etiquetas.");
        v.RuleFor(x => x.GuardianName).MaximumLength(150);
        v.RuleFor(x => x.GuardianDocumentType).Must(IsEnum<DocumentType>).WithMessage("Tipo de documento del acudiente no válido.");
        v.RuleFor(x => x.GuardianDocumentNumber).MaximumLength(30);
        v.RuleFor(x => x.GuardianPhone).MaximumLength(30);
        v.RuleFor(x => x.GuardianRelationship).MaximumLength(50);
    }

    private static bool IsEnum<TEnum>(string? text) where TEnum : struct, Enum
        => EnumText.TryParse<TEnum>(text, out _);
}

public sealed class CreateMemberRequestValidator : AbstractValidator<CreateMemberRequest>
{
    public CreateMemberRequestValidator(IClock clock)
    {
        MemberProfileRules.Apply(this, clock);

        // Solo las fichas nuevas la exigen: las anteriores a la v2 pueden no tenerla.
        RuleFor(x => x.BirthDate).NotNull().WithMessage("La fecha de nacimiento es obligatoria.");
    }
}

public sealed class UpdateMemberRequestValidator : AbstractValidator<UpdateMemberRequest>
{
    public UpdateMemberRequestValidator(IClock clock)
    {
        MemberProfileRules.Apply(this, clock);
    }
}

public sealed class RegisterMemberRequestValidator : AbstractValidator<RegisterMemberRequest>
{
    public RegisterMemberRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(6, 16);
        RuleFor(x => x.DocumentNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(256);
    }
}

public sealed class AddMemberNoteRequestValidator : AbstractValidator<AddMemberNoteRequest>
{
    public AddMemberNoteRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(MemberNote.MaxLength);
    }
}
