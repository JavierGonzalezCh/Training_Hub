using FluentValidation;
using GSM.Application.Equipment.Contracts;
using GSM.Domain.Equipment;

namespace GSM.Application.Equipment.Validators;

/// <summary>Validación de forma (400). Las invariantes de dominio (422) viven en <c>EquipmentItem</c>.</summary>
public sealed class SaveEquipmentRequestValidator : AbstractValidator<SaveEquipmentRequest>
{
    public SaveEquipmentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, EquipmentModel.NameMaxLength);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Zone).MaximumLength(EquipmentItem.ZoneMaxLength);
        RuleFor(x => x.Brand).MaximumLength(EquipmentItem.ShortTextMaxLength);
        RuleFor(x => x.Model).MaximumLength(EquipmentItem.ShortTextMaxLength);
        RuleFor(x => x.Instructions).MaximumLength(EquipmentModel.TextMaxLength);
        RuleFor(x => x.Notes).MaximumLength(EquipmentModel.TextMaxLength);
        RuleFor(x => x.Quantity).InclusiveBetween(1, EquipmentItem.MaxQuantity);
    }
}

public sealed class ChangeEquipmentStatusRequestValidator : AbstractValidator<ChangeEquipmentStatusRequest>
{
    public ChangeEquipmentStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(EquipmentStatusChange.ReasonMaxLength);
    }
}

public sealed class ReportEquipmentIssueRequestValidator : AbstractValidator<ReportEquipmentIssueRequest>
{
    public ReportEquipmentIssueRequestValidator()
    {
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(EquipmentIssue.DescriptionMaxLength);
    }
}

public sealed class UpdateEquipmentIssueRequestValidator : AbstractValidator<UpdateEquipmentIssueRequest>
{
    public UpdateEquipmentIssueRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Resolution).MaximumLength(EquipmentIssue.ResolutionMaxLength);
    }
}

public sealed class AddEquipmentFromModelsRequestValidator : AbstractValidator<AddEquipmentFromModelsRequest>
{
    public AddEquipmentFromModelsRequestValidator()
    {
        RuleFor(x => x.Items).NotNull().NotEmpty().Must(i => i is null || i.Count <= 100)
            .WithMessage("Se pueden agregar hasta 100 modelos a la vez.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ModelId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, EquipmentItem.MaxQuantity);
            item.RuleFor(i => i.Zone).MaximumLength(EquipmentItem.ZoneMaxLength);
        });
    }
}
