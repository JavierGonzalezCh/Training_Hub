using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Members;

/// <summary>
/// Texto de una autorización que el gym publica (Members v2, §3.4). Cada publicación es una fila
/// nueva con la versión siguiente: las anteriores y las aceptaciones que apuntan a ellas no se tocan.
/// Si el gym no ha publicado ninguna, vale el texto base del sistema (versión <see cref="BaseVersion"/>,
/// que no se persiste).
/// </summary>
public sealed class ConsentDocument : BaseEntity, ITenantEntity
{
    /// <summary>Versión del texto base del sistema, el que rige mientras el gym no publique el suyo.</summary>
    public const int BaseVersion = 0;

    public const int MinTextLength = 20;
    public const int MaxTextLength = 20000;

    private ConsentDocument()
    {
    }

    public Guid GymId { get; private set; }

    public ConsentType Type { get; private set; }

    public int Version { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>Desde cuándo rige esta versión.</summary>
    public DateTimeOffset EffectiveFrom { get; private set; }

    public Guid? PublishedByUserId { get; private set; }

    public static ConsentDocument Publish(
        Guid gymId, ConsentType type, int version, string text, DateTimeOffset effectiveFrom, Guid? publishedByUserId)
    {
        if (gymId == Guid.Empty || version < 1)
        {
            throw new ValidationDomainException("Documento de autorización inválido.");
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length < MinTextLength || trimmed.Length > MaxTextLength)
        {
            throw new ValidationDomainException(
                $"El texto de la autorización debe tener entre {MinTextLength} y {MaxTextLength} caracteres.");
        }

        return new ConsentDocument
        {
            GymId = gymId,
            Type = type,
            Version = version,
            Text = trimmed,
            EffectiveFrom = effectiveFrom,
            PublishedByUserId = publishedByUserId,
        };
    }
}
