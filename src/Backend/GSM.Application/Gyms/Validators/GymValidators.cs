using FluentValidation;
using GSM.Application.Gyms.Contracts;

namespace GSM.Application.Gyms.Validators;

internal static class GymRules
{
    public const string SlugPattern = "^[a-z0-9]+(-[a-z0-9]+)*$";
    public const string HexColorPattern = "^#(?i)[0-9a-f]{6}$";
}

public sealed class CreateGymRequestValidator : AbstractValidator<CreateGymRequest>
{
    public CreateGymRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Slug).NotEmpty().Length(2, 60).Matches(GymRules.SlugPattern);
        RuleFor(x => x.Nit).MaximumLength(20);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Plan).IsInEnum();

        RuleFor(x => x.Owner).NotNull();
        RuleFor(x => x.Owner.Email).NotEmpty().EmailAddress().MaximumLength(256)
            .When(x => x.Owner is not null);
        RuleFor(x => x.Owner.FullName).NotEmpty().MaximumLength(200)
            .When(x => x.Owner is not null);
        RuleFor(x => x.Owner.Password).NotEmpty().MinimumLength(10).MaximumLength(256)
            .When(x => x.Owner is not null);
    }
}

public sealed class UpdateGymRequestValidator : AbstractValidator<UpdateGymRequest>
{
    public UpdateGymRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Nit).MaximumLength(20);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.LogoUrl).MaximumLength(2048);
        RuleFor(x => x.PrimaryColor).NotEmpty().Matches(GymRules.HexColorPattern);
        RuleFor(x => x.SecondaryColor).NotEmpty().Matches(GymRules.HexColorPattern);
        RuleFor(x => x.Capacity).InclusiveBetween(1, GSM.Domain.Gyms.Gym.MaxCapacity).When(x => x.Capacity is not null);

        RuleFor(x => x.LegalName).MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(300).WithMessage("La dirección es obligatoria.");
        RuleFor(x => x.City).NotEmpty().MaximumLength(100).WithMessage("La ciudad es obligatoria.");
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude is not null);
        RuleFor(x => x.Longitude).NotNull().When(x => x.Latitude is not null)
            .WithMessage("La longitud es obligatoria si indicas la latitud.");
        RuleFor(x => x.Latitude).NotNull().When(x => x.Longitude is not null)
            .WithMessage("La latitud es obligatoria si indicas la longitud.");
        RuleFor(x => x.GeofenceRadiusMeters)
            .InclusiveBetween(GSM.Domain.Gyms.Gym.MinGeofenceRadiusMeters, GSM.Domain.Gyms.Gym.MaxGeofenceRadiusMeters)
            .When(x => x.GeofenceRadiusMeters is not null);
        RuleFor(x => x.InstagramUrl).Must(IsUrl).When(x => !string.IsNullOrWhiteSpace(x.InstagramUrl))
            .WithMessage("La URL de Instagram debe empezar por http:// o https://.");
        RuleFor(x => x.FacebookUrl).Must(IsUrl).When(x => !string.IsNullOrWhiteSpace(x.FacebookUrl))
            .WithMessage("La URL de Facebook debe empezar por http:// o https://.");
        RuleFor(x => x.TikTokUrl).Must(IsUrl).When(x => !string.IsNullOrWhiteSpace(x.TikTokUrl))
            .WithMessage("La URL de TikTok debe empezar por http:// o https://.");
        RuleFor(x => x.WebsiteUrl).Must(IsUrl).When(x => !string.IsNullOrWhiteSpace(x.WebsiteUrl))
            .WithMessage("La URL del sitio web debe empezar por http:// o https://.");
        RuleFor(x => x.WhatsAppPhone).MaximumLength(30);
    }

    private static bool IsUrl(string? value) => GSM.Domain.Gyms.Gym.IsValidUrl(value);
}

public sealed class UpdateFeatureFlagsRequestValidator : AbstractValidator<UpdateFeatureFlagsRequest>
{
    // Todos los campos son bool no anulables; el binder ya garantiza forma. Sin reglas extra.
}

public sealed class TimeSlotDtoValidator : AbstractValidator<GSM.Application.Gyms.Contracts.TimeSlotDto>
{
    public TimeSlotDtoValidator()
    {
        RuleFor(x => x.Opens).Must(OpeningHoursSupport.IsValidTime).WithMessage("La hora de apertura debe tener el formato HH:mm.");
        RuleFor(x => x.Closes).Must(OpeningHoursSupport.IsValidTime).WithMessage("La hora de cierre debe tener el formato HH:mm.");
    }
}

public sealed class UpdateOpeningHoursRequestValidator : AbstractValidator<UpdateOpeningHoursRequest>
{
    public UpdateOpeningHoursRequestValidator()
    {
        RuleFor(x => x.Days).NotNull();
        RuleForEach(x => x.Days).ChildRules(day =>
        {
            day.RuleFor(d => d.Day).NotEmpty();
            day.RuleFor(d => d.Slots).NotNull();
            day.RuleForEach(d => d.Slots).SetValidator(new TimeSlotDtoValidator());
        });
    }
}

public sealed class UpsertSpecialDayRequestValidator : AbstractValidator<UpsertSpecialDayRequest>
{
    public UpsertSpecialDayRequestValidator()
    {
        RuleFor(x => x.Note).MaximumLength(GSM.Domain.Gyms.GymSpecialDay.MaxNoteLength);
        RuleForEach(x => x.Slots).SetValidator(new TimeSlotDtoValidator());
    }
}
