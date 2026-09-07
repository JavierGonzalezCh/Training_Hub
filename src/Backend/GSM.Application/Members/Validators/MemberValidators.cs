using FluentValidation;
using GSM.Application.Members.Contracts;

namespace GSM.Application.Members.Validators;

public sealed class CreateMemberRequestValidator : AbstractValidator<CreateMemberRequest>
{
    public CreateMemberRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.EmergencyContact).MaximumLength(200);
        RuleFor(x => x.PhotoUrl).MaximumLength(2048);
        RuleFor(x => x.BirthDate).Must(BeInThePast).WithMessage("La fecha de nacimiento debe ser pasada.");
    }

    private static bool BeInThePast(DateOnly? date)
        => date is null || date < DateOnly.FromDateTime(DateTime.UtcNow);
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

public sealed class UpdateMemberRequestValidator : AbstractValidator<UpdateMemberRequest>
{
    public UpdateMemberRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.EmergencyContact).MaximumLength(200);
        RuleFor(x => x.PhotoUrl).MaximumLength(2048);
        RuleFor(x => x.BirthDate)
            .Must(d => d is null || d < DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("La fecha de nacimiento debe ser pasada.");
    }
}
