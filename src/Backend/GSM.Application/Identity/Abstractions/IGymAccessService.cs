using GSM.Domain.Identity;

namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Accesos de una persona a gyms (<c>GymAccess</c>). Implementado en Infrastructure. No está bajo el
/// Global Query Filter: se consulta por persona, a través de varios gyms.
/// </summary>
public interface IGymAccessService
{
    /// <summary>Accesos activos de la persona, el último usado primero (los nunca usados, después).</summary>
    Task<IReadOnlyList<GymAccessRecord>> ListActiveAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>El acceso, solo si pertenece a la persona y está activo. <c>null</c> si es ajeno, no existe o está inactivo.</summary>
    Task<GymAccessRecord?> GetActiveAsync(Guid userId, Guid accessId, CancellationToken cancellationToken = default);

    /// <summary>El acceso activo de la persona en ese gym con ese rol (el rol decide si es de equipo o de miembro).</summary>
    Task<GymAccessRecord?> FindActiveAsync(
        Guid userId, Guid gymId, UserRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permisos efectivos del acceso (§5): todos si es owner; el perfil + agregados − quitados si es
    /// staff; ninguno si es miembro. Es lo que viaja en el claim <c>perm</c> al emitir la sesión.
    /// </summary>
    Task<IReadOnlySet<StaffPermission>> GetEffectivePermissionsAsync(
        GymAccessRecord access, CancellationToken cancellationToken = default);

    Task MarkUsedAsync(Guid accessId, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed record GymAccessRecord(
    Guid Id,
    Guid UserId,
    Guid GymId,
    UserRole Role,
    DateTimeOffset? LastUsedAt);
