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

    /// <summary>Estado guardado del premio (<c>none</c>, <c>pending</c> o <c>delivered</c>); el vencido se calcula con <see cref="EffectiveRewardStatus"/>.</summary>
    public RewardStatus RewardStatus { get; private set; } = RewardStatus.None;

    /// <summary>Texto del premio al ganar el logro (copia: editar el logro después no cambia lo prometido).</summary>
    public string? RewardDescription { get; private set; }

    /// <summary>Hasta cuándo se puede reclamar. Nulo = no vence.</summary>
    public DateTimeOffset? RewardExpiresAt { get; private set; }

    public DateTimeOffset? RewardDeliveredAt { get; private set; }

    /// <summary>Cuenta del staff que entregó el premio.</summary>
    public Guid? RewardDeliveredBy { get; private set; }

    public static MemberAchievement Unlock(
        Guid gymId, Guid memberId, Guid achievementId, DateTimeOffset unlockedAt,
        string? rewardDescription = null, int? rewardValidDays = null)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || achievementId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de desbloqueo incompletos.");
        }

        var row = new MemberAchievement
        {
            GymId = gymId,
            MemberId = memberId,
            AchievementId = achievementId,
            UnlockedAt = unlockedAt,
        };
        row.IssueReward(rewardDescription, rewardValidDays, unlockedAt);
        return row;
    }

    /// <summary>Otorgamiento manual del staff (L4): el motivo es obligatorio.</summary>
    public static MemberAchievement GrantManual(
        Guid gymId, Guid memberId, Guid achievementId, Guid grantedBy, string reason, DateTimeOffset at,
        string? rewardDescription = null, int? rewardValidDays = null)
    {
        var row = Unlock(gymId, memberId, achievementId, at, rewardDescription, rewardValidDays);
        row.GrantedBy = RequireUser(grantedBy);
        row.GrantReason = RequireReason(reason);
        return row;
    }

    /// <summary>Vuelve a otorgar un logro manual que se había retirado: reabre la misma fila.</summary>
    public void RegrantManual(
        Guid grantedBy, string reason, DateTimeOffset at,
        string? rewardDescription = null, int? rewardValidDays = null)
    {
        if (!IsRevoked)
        {
            throw new ConflictException("El miembro ya tiene este logro.");
        }

        // Un premio ya entregado no se entrega dos veces; uno pendiente o vencido se vuelve a emitir con el plazo de hoy.
        if (RewardStatus != RewardStatus.Delivered)
        {
            IssueReward(rewardDescription, rewardValidDays, at);
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

    /// <summary>
    /// Estado del premio a la hora <paramref name="now"/> (L5). Un logro retirado no tiene premio que entregar.
    /// Pendiente con el plazo vencido es <see cref="RewardStatus.Expired"/>: se calcula, no se persiste.
    /// </summary>
    public RewardStatus EffectiveRewardStatus(DateTimeOffset now)
    {
        if (RewardStatus == RewardStatus.Delivered)
        {
            return RewardStatus.Delivered;
        }

        if (RewardStatus == RewardStatus.None || IsRevoked)
        {
            return RewardStatus.None;
        }

        return RewardExpiresAt is { } expires && now >= expires ? RewardStatus.Expired : RewardStatus.Pending;
    }

    /// <summary>Recepción entrega el premio (L5). 409 si no hay premio, ya se entregó, venció o el logro fue retirado.</summary>
    public void DeliverReward(Guid deliveredBy, DateTimeOffset now)
    {
        if (deliveredBy == Guid.Empty)
        {
            throw new ValidationDomainException("Falta quién entrega el premio.");
        }

        switch (EffectiveRewardStatus(now))
        {
            case RewardStatus.Pending:
                RewardStatus = RewardStatus.Delivered;
                RewardDeliveredAt = now;
                RewardDeliveredBy = deliveredBy;
                break;
            case RewardStatus.Delivered:
                throw new ConflictException("El premio ya fue entregado.");
            case RewardStatus.Expired:
                throw new ConflictException("El premio venció y ya no se puede entregar.");
            default:
                throw new ConflictException("Este logro no tiene un premio pendiente.");
        }
    }

    private void IssueReward(string? description, int? validDays, DateTimeOffset at)
    {
        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        RewardDescription = text;
        RewardStatus = text is null ? RewardStatus.None : RewardStatus.Pending;
        RewardExpiresAt = text is null || validDays is null ? null : at.AddDays(validDays.Value);
        RewardDeliveredAt = null;
        RewardDeliveredBy = null;
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
