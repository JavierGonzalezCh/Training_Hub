namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Quita el acceso de miembro de una cuenta a un gym (anonimización, Members §11) y revoca sus sesiones. Si con eso la
/// cuenta se queda sin ningún acceso y no es SuperAdmin, la elimina (libera el email). Idempotente. Implementado en
/// Infrastructure.
/// </summary>
public interface IMemberAccessRemoval
{
    Task<MemberAccessRemovalResult> RemoveAsync(Guid userId, Guid gymId, CancellationToken cancellationToken = default);
}

/// <param name="AccessRemoved">Había un acceso de miembro en ese gym y se quitó.</param>
/// <param name="AccountDeleted">La cuenta quedó sin accesos y se eliminó.</param>
public sealed record MemberAccessRemovalResult(bool AccessRemoved, bool AccountDeleted);
