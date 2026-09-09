using GSM.Application.SuperAdmin.Public;
using GSM.Domain.Gyms;

namespace GSM.Application.Gyms.Contracts;

public sealed record CreateGymRequest(
    string Name,
    string Slug,
    string? Nit,
    string? Phone,
    string? Email,
    GymPlan Plan,
    CreateGymOwner Owner);

public sealed record CreateGymOwner(string Email, string FullName, string Password);

public sealed record CreateGymResponse(Guid GymId, Guid OwnerId);

public sealed record UpdateGymRequest(
    string Name,
    string? Nit,
    string? Phone,
    string? Email,
    string? LogoUrl,
    string PrimaryColor,
    string SecondaryColor);

public sealed record UpdateFeatureFlagsRequest(
    bool ShowRoutines,
    bool ShowClasses,
    bool ShowProgress,
    bool ShowGamification,
    bool ShowCommunity,
    bool CheckinQr,
    bool CommunityComments,
    bool MembersCanCreateRoutines);

public sealed record GymResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Nit,
    string? Phone,
    string? Email,
    string? LogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    bool IsActive,
    string Plan,
    string Status,
    Guid OwnerId,
    DateTimeOffset CreatedAt,
    SubscriptionInfo? Subscription = null);

public sealed record GymFeatureFlagsResponse(
    bool ShowRoutines,
    bool ShowClasses,
    bool ShowProgress,
    bool ShowGamification,
    bool ShowCommunity,
    bool CheckinQr,
    bool CommunityComments,
    bool MembersCanCreateRoutines);
