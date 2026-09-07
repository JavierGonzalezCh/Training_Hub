namespace GSM.Domain.Common;

/// <summary>
/// Raíz común de toda entidad persistida. El Id se genera en el dominio (UUID v7),
/// nunca en la base de datos. Los timestamps los mantiene el DbContext en SaveChanges.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
