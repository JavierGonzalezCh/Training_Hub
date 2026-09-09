namespace GSM.Application.Gyms.Public;

/// <summary>Lectura que el módulo Gyms expone a los demás módulos.</summary>
public interface IGymReader
{
    Task<bool> ExistsAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<GymBranding?> GetBrandingAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<GymFeatureFlagsView?> GetFeatureFlagsAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Ficha del gym para SuperAdmin, incluido su estado comercial manual. <c>null</c> si no existe.</summary>
    Task<GymAdminView?> GetAdminViewAsync(Guid gymId, CancellationToken cancellationToken = default);
}

public sealed record GymAdminView(
    Guid Id,
    string Name,
    string Slug,
    string? Nit,
    string? Phone,
    string? Email,
    GSM.Domain.Gyms.GymStatus ManualStatus);

public sealed record GymBranding(
    string Name,
    string? LogoUrl,
    string PrimaryColor,
    string SecondaryColor);

public sealed record GymFeatureFlagsView(
    bool ShowRoutines,
    bool ShowClasses,
    bool ShowProgress,
    bool ShowGamification,
    bool ShowCommunity,
    bool CheckinQr,
    bool CommunityComments,
    bool MembersCanCreateRoutines);
