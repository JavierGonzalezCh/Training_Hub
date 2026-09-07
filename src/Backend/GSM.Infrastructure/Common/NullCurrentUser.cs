using GSM.Application.Common.Abstractions;
using GSM.Domain.Identity;

namespace GSM.Infrastructure.Common;

/// <summary>
/// <see cref="ICurrentUser"/> sin identidad: usado en contextos fuera de una petición HTTP
/// (migraciones en tiempo de diseño, seeds, jobs). Con esto el Global Query Filter no restringe nada.
/// </summary>
public sealed class NullCurrentUser : ICurrentUser
{
    public static readonly NullCurrentUser Instance = new();

    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public Guid? GymId => null;

    public UserRole? Role => null;

    public bool IsSuperAdmin => false;
}
