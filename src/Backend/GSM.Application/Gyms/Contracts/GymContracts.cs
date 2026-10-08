using GSM.Application.Common;
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
    string SecondaryColor,
    int? Capacity = null,
    string? LegalName = null,
    string? Address = null,
    string? City = null,
    double? Latitude = null,
    double? Longitude = null,
    int? GeofenceRadiusMeters = null,
    string? InstagramUrl = null,
    string? FacebookUrl = null,
    string? TikTokUrl = null,
    string? WebsiteUrl = null,
    string? WhatsAppPhone = null);

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
    int? Capacity,
    bool IsActive,
    string Plan,
    string Status,
    Guid OwnerId,
    DateTimeOffset CreatedAt,
    string? LegalName,
    string? Address,
    string? City,
    double? Latitude,
    double? Longitude,
    int GeofenceRadiusMeters,
    string? InstagramUrl,
    string? FacebookUrl,
    string? TikTokUrl,
    string? WebsiteUrl,
    string? WhatsAppPhone,
    SubscriptionInfo? Subscription = null);

/// <summary>
/// Lo que el miembro ve de su gimnasio ("Mi gimnasio"). Sin NIT, razón social ni datos comerciales.
/// El horario y la ocupación se sumarán con G3 y A5.
/// </summary>
public sealed record MemberGymResponse(
    string Name,
    string? LogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    string? Address,
    string? City,
    double? Latitude,
    double? Longitude,
    string? Phone,
    string? WhatsAppPhone,
    string? InstagramUrl,
    string? FacebookUrl,
    string? TikTokUrl,
    string? WebsiteUrl);

public sealed record GymFeatureFlagsResponse(
    bool ShowRoutines,
    bool ShowClasses,
    bool ShowProgress,
    bool ShowGamification,
    bool ShowCommunity,
    bool CheckinQr,
    bool CommunityComments,
    bool MembersCanCreateRoutines);
