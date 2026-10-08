using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Members;

/// <summary>
/// Una fila del historial de cambios de la ficha (Members v2, §4): un campo, su valor anterior y el
/// nuevo, quién y cuándo. Solo se añade: no tiene operaciones de edición ni de borrado.
/// </summary>
public sealed class MemberChange : BaseEntity, ITenantEntity
{
    /// <summary>Campo especial de la fila que registra el alta.</summary>
    public const string CreatedField = "created";

    public const string StatusField = "status";

    private MemberChange()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    /// <summary>Nombre estable del campo (<c>phone</c>, <c>status</c>, <c>created</c>…).</summary>
    public string Field { get; private set; } = string.Empty;

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public MemberChangeRole ChangedByRole { get; private set; }

    public MemberChangeSource Source { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Los campos de salud (M3) lo marcan; las filas sensibles solo las ve quien tenga permiso.</summary>
    public bool IsSensitive { get; private set; }

    public static MemberChange Record(
        Guid gymId,
        Guid memberId,
        string field,
        string? oldValue,
        string? newValue,
        Guid? changedByUserId,
        MemberChangeRole changedByRole,
        MemberChangeSource source,
        DateTimeOffset changedAt,
        bool isSensitive = false)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || string.IsNullOrWhiteSpace(field))
        {
            throw new ValidationDomainException("Cambio de ficha inválido.");
        }

        return new MemberChange
        {
            GymId = gymId,
            MemberId = memberId,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedByUserId = changedByUserId,
            ChangedByRole = changedByRole,
            Source = source,
            ChangedAt = changedAt,
            IsSensitive = isSensitive,
        };
    }
}
