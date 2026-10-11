using GSM.Application.Equipment.Contracts;
using GSM.Domain.Equipment;

namespace GSM.Application.Equipment;

/// <summary>Mapeo manual: la imagen de referencia no sale de la entidad sino del modelo.</summary>
public static class EquipmentMapper
{
    public static EquipmentModelResponse ToResponse(EquipmentModel model)
        => new(
            model.Id,
            model.Name,
            model.Type.ToString(),
            model.ImageUrl,
            model.Description,
            model.DefaultInstructions,
            model.TrackedPerUnit);

    public static EquipmentItemResponse ToResponse(EquipmentItem item, string? modelImageUrl, DateOnly today)
        => new(
            item.Id,
            item.ModelId,
            item.Name,
            item.Type.ToString(),
            item.PhotoUrl,
            modelImageUrl,
            !string.IsNullOrEmpty(item.PhotoUrl),
            item.Zone,
            item.Brand,
            item.Model,
            item.Instructions,
            item.Quantity,
            item.Status.ToString(),
            item.ExpectedBackOn,
            item.AcquiredOn,
            item.Notes,
            item.IsActive,
            item.Status != EquipmentStatus.Available && item.ExpectedBackOn is { } back && back < today);
}
