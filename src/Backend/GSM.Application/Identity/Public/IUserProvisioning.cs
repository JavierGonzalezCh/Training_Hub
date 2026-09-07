using GSM.Domain.Identity;

namespace GSM.Application.Identity.Public;

/// <summary>
/// Única escritura que Identity expone a otros módulos: dar de alta un usuario ya asociado a un gym
/// (owner o staff en el alta del gym). Se ejecuta dentro de la misma transacción del caso de uso.
/// </summary>
public interface IUserProvisioning
{
    Task<Guid> CreateGymUserAsync(
        string email,
        string fullName,
        string password,
        UserRole role,
        Guid gymId,
        CancellationToken cancellationToken = default);
}
