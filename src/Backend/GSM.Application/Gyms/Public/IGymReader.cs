namespace GSM.Application.Gyms.Public;

/// <summary>Lectura que el módulo Gyms expone a los demás módulos.</summary>
public interface IGymReader
{
    Task<bool> ExistsAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<GymBranding?> GetBrandingAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// La marca del gym por su slug, para las pantallas anónimas (login del miembro). El slug se compara en
    /// minúsculas; <c>null</c> si no existe.
    /// </summary>
    Task<GymBranding?> GetBrandingBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Aforo del gym (personas a la vez); <c>null</c> si no lo configuró o el gym no existe (A5).</summary>
    Task<int?> GetCapacityAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<GymFeatureFlagsView?> GetFeatureFlagsAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Ficha del gym para SuperAdmin, incluido su estado comercial manual. <c>null</c> si no existe.</summary>
    Task<GymAdminView?> GetAdminViewAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cómo está el gym en el instante dado, según su horario (semana, festivos de Colombia y excepciones),
    /// con la hora local del gym. <b>Un gym sin horario cargado está siempre abierto.</b>
    /// </summary>
    Task<GSM.Domain.Gyms.GymOpenStatus> GetOpenStatusAsync(
        Guid gymId, DateTimeOffset instant, CancellationToken cancellationToken = default);

    /// <summary>Igual que <see cref="GetOpenStatusAsync"/> pero con la fecha y hora locales del gym ya resueltas.</summary>
    Task<GSM.Domain.Gyms.GymOpenStatus> GetOpenStatusAtLocalAsync(
        Guid gymId, DateOnly localDate, TimeOnly localTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Horario del gym (semana, festivos y excepciones del rango <c>[from, to]</c>) para que otro módulo
    /// evalúe tramos completos, no solo un instante. Solo lectura. Sin horario cargado,
    /// <see cref="GSM.Domain.Gyms.OpeningSchedule.HasSchedule"/> es <c>false</c> (nada se rechaza).
    /// </summary>
    Task<GSM.Domain.Gyms.OpeningSchedule> GetOpeningScheduleAsync(
        Guid gymId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>¿Está abierto el gym en ese instante? Sin horario cargado, siempre <c>true</c>.</summary>
    Task<bool> IsOpenAtAsync(Guid gymId, DateTimeOffset instant, CancellationToken cancellationToken = default);
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
