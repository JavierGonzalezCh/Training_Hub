using FluentValidation;
using GSM.Application.Memberships.Contracts;

namespace GSM.Application.Memberships.Validators;

public sealed class CreatePlanRequestValidator : AbstractValidator<CreatePlanRequest>
{
    public CreatePlanRequestValidator()
    {
        RuleFor(x => x.Seats!.Value).InclusiveBetween(1, GSM.Domain.Memberships.MembershipPlan.MaxSeats).When(x => x.Seats.HasValue);
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DurationUnit).Must(PlanDuration.IsKnown).WithMessage("La unidad debe ser days o months.");
        RuleFor(x => x.DurationValue).InclusiveBetween(1, 3650).When(x => !IsPass(x.Kind));
        RuleFor(x => x.Kind).Must(PlanDuration.IsKnownKind).WithMessage("El tipo debe ser time o pass.");
        RuleFor(x => x.ValidityDays!.Value).InclusiveBetween(1, 3650).When(x => x.ValidityDays.HasValue)
            .WithMessage("La vigencia debe estar entre 1 y 3650 días.");
        RuleFor(x => x.Entries!.Value).InclusiveBetween(1, GSM.Domain.Memberships.MembershipPlan.MaxEntries).When(x => x.Entries.HasValue)
            .WithMessage("Las entradas deben estar entre 1 y 1000.");
        RuleFor(x => x.MaxFreezeDays!.Value).InclusiveBetween(0, GSM.Domain.Memberships.MembershipPlan.MaxFreezeDaysLimit).When(x => x.MaxFreezeDays.HasValue);
        RuleFor(x => x.MinFreezeDays!.Value).InclusiveBetween(1, GSM.Domain.Memberships.MembershipPlan.MaxFreezeDaysLimit).When(x => x.MinFreezeDays.HasValue);
        RuleFor(x => x.DurationValue).InclusiveBetween(1, GSM.Domain.Memberships.MembershipPlan.MaxMonths)
            .When(x => PlanDuration.IsKnown(x.DurationUnit) && PlanDuration.ParseUnit(x.DurationUnit) == GSM.Domain.Memberships.DurationUnit.Months && !IsPass(x.Kind));
    }

    private static bool IsPass(string? kind) => PlanDuration.IsKnownKind(kind) && PlanDuration.ParseKind(kind) == GSM.Domain.Memberships.PlanKind.Pass;
}

public sealed class UpdatePlanRequestValidator : AbstractValidator<UpdatePlanRequest>
{
    public UpdatePlanRequestValidator()
    {
        RuleFor(x => x.Seats!.Value).InclusiveBetween(1, GSM.Domain.Memberships.MembershipPlan.MaxSeats).When(x => x.Seats.HasValue);
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DurationUnit).Must(PlanDuration.IsKnown).WithMessage("La unidad debe ser days o months.");
        RuleFor(x => x.DurationValue).InclusiveBetween(1, 3650).When(x => !IsPass(x.Kind));
        RuleFor(x => x.Kind).Must(PlanDuration.IsKnownKind).WithMessage("El tipo debe ser time o pass.");
        RuleFor(x => x.ValidityDays!.Value).InclusiveBetween(1, 3650).When(x => x.ValidityDays.HasValue)
            .WithMessage("La vigencia debe estar entre 1 y 3650 días.");
        RuleFor(x => x.Entries!.Value).InclusiveBetween(1, GSM.Domain.Memberships.MembershipPlan.MaxEntries).When(x => x.Entries.HasValue)
            .WithMessage("Las entradas deben estar entre 1 y 1000.");
        RuleFor(x => x.MaxFreezeDays!.Value).InclusiveBetween(0, GSM.Domain.Memberships.MembershipPlan.MaxFreezeDaysLimit).When(x => x.MaxFreezeDays.HasValue);
        RuleFor(x => x.MinFreezeDays!.Value).InclusiveBetween(1, GSM.Domain.Memberships.MembershipPlan.MaxFreezeDaysLimit).When(x => x.MinFreezeDays.HasValue);
        RuleFor(x => x.DurationValue).InclusiveBetween(1, GSM.Domain.Memberships.MembershipPlan.MaxMonths)
            .When(x => PlanDuration.IsKnown(x.DurationUnit) && PlanDuration.ParseUnit(x.DurationUnit) == GSM.Domain.Memberships.DurationUnit.Months && !IsPass(x.Kind));
    }

    private static bool IsPass(string? kind) => PlanDuration.IsKnownKind(kind) && PlanDuration.ParseKind(kind) == GSM.Domain.Memberships.PlanKind.Pass;
}

public sealed class AssignMembershipRequestValidator : AbstractValidator<AssignMembershipRequest>
{
    public AssignMembershipRequestValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.StartDateReason).MaximumLength(500);
        RuleFor(x => x.AgreedPrice).GreaterThanOrEqualTo(0).When(x => x.AgreedPrice.HasValue);
        RuleFor(x => x.DiscountReason).MaximumLength(500);
        RuleFor(x => x.EnrollmentWaivedReason).MaximumLength(500);
    }
}

public sealed class AssignGroupMembershipRequestValidator : AbstractValidator<AssignGroupMembershipRequest>
{
    public AssignGroupMembershipRequestValidator()
    {
        RuleFor(x => x.HolderMemberId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.BeneficiaryMemberIds).NotNull();
        RuleFor(x => x.BeneficiaryMemberIds.Count).LessThanOrEqualTo(GSM.Domain.Memberships.MembershipPlan.MaxSeats)
            .When(x => x.BeneficiaryMemberIds is not null);
        RuleForEach(x => x.BeneficiaryMemberIds).NotEmpty();
        RuleFor(x => x.StartDateReason).MaximumLength(500);
        RuleFor(x => x.AgreedPrice).GreaterThanOrEqualTo(0).When(x => x.AgreedPrice.HasValue);
        RuleFor(x => x.DiscountReason).MaximumLength(500);
        RuleFor(x => x.EnrollmentWaivedReason).MaximumLength(500);
    }
}

public sealed class RenewGroupMembershipRequestValidator : AbstractValidator<RenewGroupMembershipRequest>
{
    public RenewGroupMembershipRequestValidator()
    {
        RuleFor(x => x.PlanId!.Value).NotEmpty().When(x => x.PlanId.HasValue);
        RuleForEach(x => x.BeneficiaryMemberIds).NotEmpty().When(x => x.BeneficiaryMemberIds is not null);
        RuleFor(x => x.StartDateReason).MaximumLength(500);
        RuleFor(x => x.AgreedPrice).GreaterThanOrEqualTo(0).When(x => x.AgreedPrice.HasValue);
        RuleFor(x => x.DiscountReason).MaximumLength(500);
    }
}

public sealed class AddGroupBeneficiaryRequestValidator : AbstractValidator<AddGroupBeneficiaryRequest>
{
    public AddGroupBeneficiaryRequestValidator() => RuleFor(x => x.MemberId).NotEmpty();
}

public sealed class UpdateMembershipSettingsRequestValidator : AbstractValidator<UpdateMembershipSettingsRequest>
{
    public UpdateMembershipSettingsRequestValidator()
    {
        RuleFor(x => x.GraceDays).InclusiveBetween(0, GSM.Domain.Memberships.MembershipSettings.MaxGraceDays);
        RuleFor(x => x.ExpiringDays).InclusiveBetween(1, GSM.Domain.Memberships.MembershipSettings.MaxExpiringDays);
        RuleFor(x => x.EnrollmentFee).InclusiveBetween(0, GSM.Domain.Memberships.MembershipSettings.MaxEnrollmentFee);
    }
}

public sealed class CancelMembershipRequestValidator : AbstractValidator<CancelMembershipRequest>
{
    public CancelMembershipRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(500);
    }
}

public sealed class ChangePlanRequestValidator : AbstractValidator<ChangePlanRequest>
{
    public ChangePlanRequestValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.AgreedPrice!.Value).InclusiveBetween(0m, 100_000_000m).When(x => x.AgreedPrice.HasValue);
        RuleFor(x => x.DiscountReason).MaximumLength(500);
        RuleFor(x => x.CreditOverride!.Value).InclusiveBetween(0m, 100_000_000m).When(x => x.CreditOverride.HasValue);
        RuleFor(x => x.CreditAdjustmentReason).MaximumLength(500);
    }
}
