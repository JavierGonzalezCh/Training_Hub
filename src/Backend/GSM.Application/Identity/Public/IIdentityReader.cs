namespace GSM.Application.Identity.Public;

/// <summary>
/// Único punto de lectura que Identity expone a otros módulos. Sin acceso a la entidad,
/// al DbContext ni al store.
/// </summary>
public interface IIdentityReader
{
    Task<IdentityUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>La persona. El gym y el rol ya no son de la cuenta sino de cada acceso (<c>GymAccess</c>).</summary>
public sealed record IdentityUserDto(Guid Id, string FullName, bool IsSuperAdmin);
