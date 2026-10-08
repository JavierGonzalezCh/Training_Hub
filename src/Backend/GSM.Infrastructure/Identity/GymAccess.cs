using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Acceso de una persona (<see cref="AppUser"/>) a un gym con un rol. Una persona tiene como máximo
/// dos por gym: uno de equipo (<c>owner</c> o <c>staff</c>) y uno de <c>member</c>
/// (índice único <c>(user_id, gym_id, is_team)</c>). El SuperAdmin no tiene accesos: es global.
/// </summary>
/// <remarks>No implementa <c>ITenantEntity</c>: se consulta siempre por persona, a través de varios gyms.</remarks>
public sealed class GymAccess : BaseEntity
{
    private GymAccess()
    {
    }

    public Guid UserId { get; private set; }

    public Guid GymId { get; private set; }

    public UserRole Role { get; private set; }

    /// <summary><c>true</c> para <c>owner</c> y <c>staff</c>. Persistido para sostener el índice único.</summary>
    public bool IsTeam { get; private set; }

    /// <summary>Perfil de staff (§5.2 de Identity). Solo para <c>staff</c>; <c>null</c> en owner y miembro.</summary>
    public StaffProfile? Profile { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary>Última vez que se emitió una sesión para este acceso: ordena el selector (el último usado, primero).</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <param name="profile">Solo para staff; sin indicarlo, un staff nace con el perfil de menos privilegio (Recepción).</param>
    public static GymAccess Grant(Guid userId, Guid gymId, UserRole role, StaffProfile? profile = null)
    {
        if (role == UserRole.SuperAdmin)
        {
            throw new ValidationDomainException("El SuperAdmin no tiene accesos a gyms.");
        }

        if (profile is not null && role != UserRole.Staff)
        {
            throw new ValidationDomainException("Solo el staff tiene perfil.");
        }

        return new GymAccess
        {
            UserId = userId,
            GymId = gymId,
            Role = role,
            IsTeam = IsTeamRole(role),
            Profile = role == UserRole.Staff ? profile ?? StaffProfile.Reception : null,
        };
    }

    public void ChangeProfile(StaffProfile profile)
    {
        if (Role != UserRole.Staff)
        {
            throw new ValidationDomainException("Solo el staff tiene perfil.");
        }

        Profile = profile;
    }

    public static bool IsTeamRole(UserRole role) => role is UserRole.Owner or UserRole.Staff;

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DeactivatedAt = now;
    }

    public void Reactivate()
    {
        IsActive = true;
        DeactivatedAt = null;
    }

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;
}
