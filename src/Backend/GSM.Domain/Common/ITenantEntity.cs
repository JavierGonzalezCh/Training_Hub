namespace GSM.Domain.Common;

/// <summary>
/// Marca una entidad como perteneciente a un tenant (gym). Activa el Global Query Filter
/// de EF Core: toda consulta se restringe a <see cref="GymId"/> == gym actual,
/// salvo que el usuario sea SuperAdmin.
/// </summary>
public interface ITenantEntity
{
    Guid GymId { get; }
}
