using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Achievements;

/// <summary>
/// Logro desbloqueado por un miembro. Único por <c>(member_id, achievement_id)</c>.
/// Los otorgados por el staff (L4) guardan quién y por qué, y son los únicos que se pueden retirar: el retiro
/// no borra la fila (queda quién, cuándo y por qué) pero el logro deja de contar como ganado.
/// </summary>
public sealed class MemberAchievement : BaseEntity, ITenantEntity
{
    private MemberAchievement()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public Guid AchievementId { get; private set; }

    public DateTimeOffset UnlockedAt { get; private set; }

    /// <summary>Cuenta del staff que lo otorgó. Nulo en los logros automáticos.</summary>
    public Guid? GrantedBy { get; private set; }

    /// <summary>Motivo del otorgamiento manual. Nulo en los logros automáticos.</summary>
    public string? GrantReason { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public string? RevokeReason { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public static MemberAchievement Unlock(
        Guid gymId, Guid memberId, Guid achievementId, DateTimeOffset unlockedAt)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || achievementId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de desbloqueo incompletos.");
        }

        return new MemberAchievement
        {
            GymId = gymId,
            MemberId = memberId,
            AchievementId = achievementId,
            UnlockedAt = unlockedAt,
        };
    }

    /// <summary>Otorgamiento manual del staff (L4): el motivo es obligatorio.</summary>
    public static MemberAchievement GrantManual(
        Guid gymId, Guid memberId, Guid achievementId, Guid grantedBy, string reason, DateTimeOffset at)
    {
        var row = Unlock(gymId, memberId, achievementId, at);
        row.GrantedBy = RequireUser(grantedBy);
        row.GrantReason = RequireReason(reason);
        return row;
    }

    /// <summary>Vuelve a otorgar un logro manual que se había retirado: reabre la misma fila.</summary>
    public void RegrantManual(Guid grantedBy, string reason, DateTimeOffset at)
    {
        if (!IsRevoked)
        {
            throw new ConflictException("El miembro ya tiene este logro.");
        }

        GrantedBy = RequireUser(grantedBy);
        GrantReason = RequireReason(reason);
        UnlockedAt = at;
        RevokedAt = null;
        RevokedBy = null;
        RevokeReason = null;
    }

    /// <summary>Retira un logro manual (otorgado por error). Los automáticos no se retiran.</summary>
    public void Revoke(Guid revokedBy, string reason, DateTimeOffset at)
    {
        if (GrantedBy is null)
        {
            throw new ConflictException("Solo se pueden retirar los logros otorgados por el staff.");
        }

        if (IsRevoked)
        {
            throw new ConflictException("El logro ya estaba retirado.");
        }

        RevokedBy = RequireUser(revokedBy);
        RevokeReason = RequireReason(reason);
        RevokedAt = at;
    }

    private static Guid RequireUser(Guid userId)
        => userId == Guid.Empty ? throw new ValidationDomainException("Falta quién otorga el logro.") : userId;

    private static string RequireReason(string reason)
    {
        var value = (reason ?? string.Empty).Trim();
        if (value.Length is < 3 or > 300)
        {
            throw new ValidationDomainException("El motivo debe tener entre 3 y 300 caracteres.");
        }

        return value;
    }
}
