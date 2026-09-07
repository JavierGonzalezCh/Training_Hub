using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Common.Abstractions;

public static class CurrentUserExtensions
{
    /// <summary>Id del gym del token. Falla con 403 si el usuario no está asociado a un gym (p. ej. superadmin).</summary>
    public static Guid RequireGymId(this ICurrentUserService currentUser)
        => currentUser.GymId
           ?? throw new ForbiddenDomainException("Esta operación requiere un usuario asociado a un gym.");
}
