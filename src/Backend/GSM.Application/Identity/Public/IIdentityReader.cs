using GSM.Domain.Identity;

namespace GSM.Application.Identity.Public;

/// <summary>
/// Único punto de lectura que Identity expone a otros módulos. Sin acceso a la entidad,
/// al DbContext ni al store.
/// </summary>
public interface IIdentityReader
{
    Task<IdentityUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record IdentityUserDto(Guid Id, string FullName, UserRole Role, Guid? GymId);
