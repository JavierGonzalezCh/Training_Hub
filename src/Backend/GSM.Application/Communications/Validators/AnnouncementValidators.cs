using FluentValidation;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Communications;

namespace GSM.Application.Communications.Validators;

/// <summary>Forma del request (400). Las mismas reglas viven en <see cref="Announcement"/> como invariantes (422).</summary>
public sealed class CreateAnnouncementRequestValidator : AbstractValidator<CreateAnnouncementRequest>
{
    public CreateAnnouncementRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty()
            .Must(t => t.Trim().Length is >= Announcement.TitleMinLength and <= Announcement.TitleMaxLength)
            .WithMessage($"El título debe tener entre {Announcement.TitleMinLength} y {Announcement.TitleMaxLength} caracteres.");
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Announcement.BodyMaxLength);
    }
}

public sealed class UpdateAnnouncementRequestValidator : AbstractValidator<UpdateAnnouncementRequest>
{
    public UpdateAnnouncementRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty()
            .Must(t => t.Trim().Length is >= Announcement.TitleMinLength and <= Announcement.TitleMaxLength)
            .WithMessage($"El título debe tener entre {Announcement.TitleMinLength} y {Announcement.TitleMaxLength} caracteres.");
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Announcement.BodyMaxLength);
    }
}

public sealed class ListAnnouncementsQueryValidator : AbstractValidator<ListAnnouncementsQuery>
{
    public ListAnnouncementsQueryValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<AnnouncementStatus>(s, ignoreCase: true, out _))
            .WithMessage("Estado inválido: Draft, Published o Archived.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
