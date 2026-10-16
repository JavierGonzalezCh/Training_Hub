using GSM.Domain.Classes;

namespace GSM.Application.Classes.Abstractions;

/// <summary>Acceso a <see cref="ClassEquipmentItem"/> (máquinas ligadas a una clase, C6). Sin <c>SaveChanges</c>.</summary>
public interface IClassEquipmentRepository
{
    Task<IReadOnlyList<ClassEquipmentItem>> ListForClassAsync(Guid classId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClassEquipmentItem>> ListForClassesAsync(
        IReadOnlyCollection<Guid> classIds, CancellationToken cancellationToken = default);

    void Add(ClassEquipmentItem link);

    void Remove(ClassEquipmentItem link);
}
