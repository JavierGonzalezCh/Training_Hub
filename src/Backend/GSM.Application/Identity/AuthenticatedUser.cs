using GSM.Domain.Identity;

namespace GSM.Application.Identity;

/// <summary>
/// La persona (cuenta): lo que no depende de a qué gym entra. El gym y el rol viven en el acceso
/// (<see cref="Abstractions.GymAccessRecord"/>).
/// </summary>
public sealed record AccountIdentity(Guid Id, string Email, string FullName, bool IsSuperAdmin);

/// <summary>
/// Identidad con la que se emite un access token: la persona vista desde un acceso concreto
/// (gym + rol). Para el SuperAdmin, <c>Role = SuperAdmin</c> y <c>GymId = null</c>.
/// </summary>
public sealed record AuthenticatedUser(
    Guid Id,
    string Email,
    string FullName,
    UserRole Role,
    Guid? GymId);
