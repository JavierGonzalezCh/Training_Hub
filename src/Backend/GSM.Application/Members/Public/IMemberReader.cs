using GSM.Domain.Members;

namespace GSM.Application.Members.Public;

/// <summary>Lectura que el módulo Members expone a los demás módulos.</summary>
public interface IMemberReader
{
    Task<MemberInfo?> GetAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Resuelve un miembro por su QR dentro del gym en contexto. <c>null</c> si no existe o es de otro gym.</summary>
    Task<MemberInfo?> ResolveByQrAsync(string qrCode, CancellationToken cancellationToken = default);

    /// <summary>Miembro vinculado a esa cuenta de Identity. <c>null</c> si el usuario no es un miembro.</summary>
    Task<MemberInfo?> ResolveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> GetActiveCountAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Socios activos por gym, en una sola consulta (panel de SuperAdmin). Un gym sin socios
    /// activos no aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetActiveCountsByGymAsync(CancellationToken cancellationToken = default);
}

public sealed record MemberInfo(
    Guid Id,
    Guid GymId,
    Guid? UserId,
    string FirstName,
    string LastName,
    MemberStatus Status,
    string QrCode);
