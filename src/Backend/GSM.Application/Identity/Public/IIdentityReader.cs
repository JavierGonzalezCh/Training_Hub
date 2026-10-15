namespace GSM.Application.Identity.Public;

/// <summary>
/// Único punto de lectura que Identity expone a otros módulos. Sin acceso a la entidad,
/// al DbContext ni al store.
/// </summary>
public interface IIdentityReader
{
    Task<IdentityUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quienes pueden ser entrenador asignado en el gym (Members M1b): el Owner y el staff con el permiso
    /// <c>Trainer</c> efectivo (perfil + ajustes), todos con el acceso activo. Ordenados por nombre.
    /// </summary>
    Task<IReadOnlyList<TrainerCandidateDto>> ListTrainersAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>¿Puede esta cuenta ser entrenador asignado en ese gym? (acceso activo y Owner o permiso <c>Trainer</c>).</summary>
    Task<bool> CanBeTrainerAsync(Guid userId, Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// N1b: las cuentas con acceso de equipo activo en el gym que tienen <b>alguno</b> de esos permisos
    /// (efectivos: perfil + ajustes). El Owner cuenta siempre.
    /// </summary>
    Task<IReadOnlySet<Guid>> ListUserIdsWithAnyPermissionAsync(
        Guid gymId, IReadOnlyCollection<GSM.Domain.Identity.StaffPermission> permissions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// I4/G4: los Owners con acceso activo al gym (cuenta activa), el más antiguo primero. Los avisos "al Owner"
    /// y el contacto de facturación se resuelven contra esta lista.
    /// </summary>
    Task<IReadOnlyList<GymOwnerDto>> ListActiveOwnersAsync(Guid gymId, CancellationToken cancellationToken = default);
}

/// <summary>Un Owner activo del gym.</summary>
public sealed record GymOwnerDto(Guid UserId, string FullName, string Email);

/// <summary>La persona. El gym y el rol ya no son de la cuenta sino de cada acceso (<c>GymAccess</c>).</summary>
public sealed record IdentityUserDto(Guid Id, string FullName, bool IsSuperAdmin);

/// <summary>Una cuenta que puede ser entrenador asignado. <c>IsOwner</c>: lo es por ser Owner, no por el permiso.</summary>
public sealed record TrainerCandidateDto(Guid UserId, string FullName, bool IsOwner);
