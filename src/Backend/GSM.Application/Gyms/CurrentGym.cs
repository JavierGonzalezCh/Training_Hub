using GSM.Application.Common.Abstractions;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Gyms;

internal static class CurrentGym
{
    /// <summary>Id del gym del token. Falla con 403 si el usuario no está asociado a un gym (p. ej. superadmin).</summary>
    public static Guid RequireId(ICurrentUserService currentUser)
        => currentUser.GymId
           ?? throw new ForbiddenDomainException("Esta operación requiere un usuario asociado a un gym.");
}
