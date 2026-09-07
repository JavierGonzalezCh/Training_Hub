using GSM.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Entidad de ASP.NET Identity. Vive en Infrastructure: Domain no conoce Identity.
/// Otros módulos solo la ven a través de <c>IIdentityReader</c>.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
    }

    public required string FullName { get; set; }

    /// <summary>Gym al que pertenece. <c>null</c> únicamente para <see cref="UserRole.SuperAdmin"/>.</summary>
    public Guid? GymId { get; set; }

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<RefreshToken> RefreshTokens { get; } = [];
}
