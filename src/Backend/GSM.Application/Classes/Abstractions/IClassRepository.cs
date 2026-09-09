using GSM.Domain.Classes;

namespace GSM.Application.Classes.Abstractions;

/// <summary>Filtro del listado de definiciones de clase.</summary>
public sealed record ClassFilter(bool IncludeInactive);

/// <summary>
/// Acceso a <see cref="GymClass"/>. El Global Query Filter ya restringe al gym actual;
/// aquí solo se filtra por id / estado. Sin <c>SaveChanges</c>.
/// </summary>
public interface IClassRepository
{
    Task<GymClass?> GetAsync(Guid classId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GymClass>> ListAsync(ClassFilter filter, CancellationToken cancellationToken = default);

    void Add(GymClass gymClass);
}
