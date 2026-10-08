using GSM.Domain.Equipment;

namespace GSM.Application.Equipment.Contracts;

/// <summary>Modelo del catálogo del sistema. <c>Type</c> sale como texto (nombre del enum).</summary>
public sealed record EquipmentModelResponse(
    Guid Id,
    string Name,
    string Type,
    string? ImageUrl,
    string? Description,
    string? DefaultInstructions,
    bool TrackedPerUnit);

/// <summary>
/// Una máquina del inventario. <c>Type</c> y <c>Status</c> salen como texto. <c>ModelImageUrl</c> es la
/// imagen de referencia del modelo (si salió del catálogo); <c>HasOwnPhoto</c> dice si el gym subió la suya.
/// </summary>
public sealed record EquipmentItemResponse(
    Guid Id,
    Guid? ModelId,
    string Name,
    string Type,
    string? PhotoUrl,
    string? ModelImageUrl,
    bool HasOwnPhoto,
    string? Zone,
    string? Brand,
    string? Model,
    string? Instructions,
    int Quantity,
    string Status,
    DateOnly? ExpectedBackOn,
    DateOnly? AcquiredOn,
    string? Notes,
    bool IsActive);

/// <summary>Crea (máquina propia) o edita una máquina. El estado no se cambia aquí (E2).</summary>
public sealed record SaveEquipmentRequest(
    string Name,
    EquipmentType Type,
    string? Zone,
    string? Brand,
    string? Model,
    string? Instructions,
    int Quantity,
    DateOnly? AcquiredOn,
    string? Notes);

/// <summary>Un modelo del catálogo que el gym tiene, con cuántos y en qué zona.</summary>
public sealed record EquipmentFromModelItem(Guid ModelId, int Quantity, string? Zone);

public sealed record AddEquipmentFromModelsRequest(IReadOnlyList<EquipmentFromModelItem> Items);
