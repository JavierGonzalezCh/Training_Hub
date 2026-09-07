using GSM.Domain.Identity;

namespace GSM.Application.Identity;

/// <summary>Proyección mínima de un usuario autenticado que circula entre las abstracciones de Identity.</summary>
public sealed record AuthenticatedUser(
    Guid Id,
    string Email,
    string FullName,
    UserRole Role,
    Guid? GymId);
