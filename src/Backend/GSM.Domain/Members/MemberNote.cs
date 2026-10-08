using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Members;

/// <summary>
/// Nota interna sobre un miembro (Members v2, §3.3). La ve todo el equipo del gym; el propio miembro
/// nunca. Solo se añade. <see cref="AuthorUserId"/> es nulo en las notas que escribe el sistema
/// (p. ej. el contacto de emergencia anterior a la v2).
/// </summary>
public sealed class MemberNote : BaseEntity, ITenantEntity
{
    public const int MaxLength = 2000;

    private MemberNote()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public Guid? AuthorUserId { get; private set; }

    public static MemberNote Add(Guid gymId, Guid memberId, string text, Guid? authorUserId)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("Nota inválida.");
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength)
        {
            throw new ValidationDomainException($"La nota es obligatoria y no puede exceder {MaxLength} caracteres.");
        }

        return new MemberNote
        {
            GymId = gymId,
            MemberId = memberId,
            Text = trimmed,
            AuthorUserId = authorUserId,
        };
    }
}
