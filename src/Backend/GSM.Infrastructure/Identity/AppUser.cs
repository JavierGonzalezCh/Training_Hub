using Microsoft.AspNetCore.Identity;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Entidad de ASP.NET Identity: la persona. Vive en Infrastructure: Domain no conoce Identity.
/// Otros módulos solo la ven a través de <c>IIdentityReader</c>.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
    }

    public required string FullName { get; set; }

    /// <summary>
    /// SuperAdmin global. La cuenta ya no lleva <c>GymId</c> ni <c>Role</c>: el gym y el rol viven en
    /// <see cref="GymAccess"/>, uno por gym. El SuperAdmin no tiene accesos.
    /// </summary>
    public bool IsSuperAdmin { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// FL1: la cuenta entró con una clave provisional (la cédula del miembro en su primer ingreso) y debe crear
    /// su contraseña antes de usar la app. Mientras esté en <c>true</c>, el token lleva el claim
    /// <c>pwd_change</c> y la API solo deja crear la contraseña, ver la sesión y salir.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; } = [];
}
