using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Equipment.Abstractions;
using GSM.Application.Equipment.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Equipment;

/// <summary>Catálogo del sistema, con filtro por tipo. Solo lectura.</summary>
public sealed class ListEquipmentModelsHandler(ICurrentUserService currentUser, IEquipmentModelRepository models)
{
    public async Task<IReadOnlyList<EquipmentModelResponse>> HandleAsync(
        EquipmentType? type, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var list = await models.ListActiveAsync(type, cancellationToken);
        return list.Select(EquipmentMapper.ToResponse).ToList();
    }
}

/// <summary>Resuelve la imagen de referencia de cada máquina que salió del catálogo.</summary>
internal static class EquipmentResponses
{
    public static async Task<IReadOnlyList<EquipmentItemResponse>> BuildAsync(
        IEnumerable<EquipmentItem> items,
        IEquipmentModelRepository models,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var list = items.ToList();
        var modelIds = list.Where(i => i.ModelId.HasValue).Select(i => i.ModelId!.Value).Distinct().ToList();
        var images = (await models.GetByIdsAsync(modelIds, cancellationToken))
            .ToDictionary(m => m.Id, m => m.ImageUrl);

        return list
            .Select(i => EquipmentMapper.ToResponse(
                i, i.ModelId is { } id ? images.GetValueOrDefault(id) : null, clock.Today))
            .ToList();
    }

    public static async Task<EquipmentItemResponse> BuildAsync(
        EquipmentItem item, IEquipmentModelRepository models, IClock clock, CancellationToken cancellationToken)
        => (await BuildAsync([item], models, clock, cancellationToken))[0];
}

public sealed class ListEquipmentHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IClock clock)
{
    public async Task<IReadOnlyList<EquipmentItemResponse>> HandleAsync(
        EquipmentFilter filter, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var list = await items.ListAsync(filter, cancellationToken);
        return await EquipmentResponses.BuildAsync(list, models, clock, cancellationToken);
    }
}

public sealed class ListEquipmentZonesHandler(ICurrentUserService currentUser, IEquipmentItemRepository items)
{
    public Task<IReadOnlyList<string>> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        return items.ListZonesAsync(cancellationToken);
    }
}

public sealed class GetEquipmentHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IClock clock)
{
    public async Task<EquipmentItemResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var item = await items.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Equipo no encontrado.");

        return await EquipmentResponses.BuildAsync(item, models, clock, cancellationToken);
    }
}

/// <summary>Máquina propia del gym, creada a mano (sin modelo del catálogo).</summary>
public sealed class CreateEquipmentHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IClock clock,
    IUnitOfWork unitOfWork,
    ILogger<CreateEquipmentHandler> logger)
{
    public async Task<EquipmentItemResponse> HandleAsync(SaveEquipmentRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var item = EquipmentItem.Create(
            gymId, null, request.Name, request.Type, request.Zone, request.Brand, request.Model,
            request.Instructions, request.Quantity, request.AcquiredOn, request.Notes);

        items.Add(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Equipo propio {EquipmentId} creado en el gym {GymId}", item.Id, gymId);
        return await EquipmentResponses.BuildAsync(item, models, clock, cancellationToken);
    }
}

public sealed class UpdateEquipmentHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IClock clock,
    IUnitOfWork unitOfWork,
    ILogger<UpdateEquipmentHandler> logger)
{
    public async Task<EquipmentItemResponse> HandleAsync(
        Guid id, SaveEquipmentRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var item = await items.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Equipo no encontrado.");

        if (!item.IsActive)
        {
            throw new ConflictException("El equipo está dado de baja y no se puede editar.");
        }

        item.Update(
            request.Name, request.Type, request.Zone, request.Brand, request.Model,
            request.Instructions, request.Quantity, request.AcquiredOn, request.Notes);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Equipo {EquipmentId} actualizado", item.Id);
        return await EquipmentResponses.BuildAsync(item, models, clock, cancellationToken);
    }
}

/// <summary>
/// "Agregar del catálogo": varios modelos de una vez. Una fila por unidad en las máquinas
/// (<c>TrackedPerUnit</c>), numeradas ("Bicicleta estática 1", "…2"); una sola fila con cantidad en los
/// implementos sueltos. Si el gym ya tiene unidades del modelo, la numeración sigue donde iba.
/// </summary>
public sealed class AddEquipmentFromModelsHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IClock clock,
    IUnitOfWork unitOfWork,
    ILogger<AddEquipmentFromModelsHandler> logger)
{
    public const int MaxUnitsPerModel = 50;

    public async Task<IReadOnlyList<EquipmentItemResponse>> HandleAsync(
        AddEquipmentFromModelsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var modelIds = request.Items.Select(i => i.ModelId).Distinct().ToList();
        var found = (await models.GetByIdsAsync(modelIds, cancellationToken))
            .Where(m => m.IsActive)
            .ToDictionary(m => m.Id);

        var missing = modelIds.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException("Alguno de los modelos del catálogo no existe o ya no está disponible.");
        }

        var created = new List<EquipmentItem>();
        foreach (var line in request.Items)
        {
            var model = found[line.ModelId];

            if (model.TrackedPerUnit)
            {
                if (line.Quantity > MaxUnitsPerModel)
                {
                    throw new ValidationDomainException(
                        $"Se registran como máximo {MaxUnitsPerModel} unidades de \"{model.Name}\" a la vez.");
                }

                var existing = await items.CountActiveByModelAsync(model.Id, cancellationToken)
                               + created.Count(c => c.ModelId == model.Id);

                for (var n = 1; n <= line.Quantity; n++)
                {
                    var total = existing + line.Quantity;
                    var name = total == 1 ? model.Name : $"{model.Name} {existing + n}";
                    created.Add(NewItem(gymId, model, name, 1, line.Zone));
                }
            }
            else
            {
                created.Add(NewItem(gymId, model, model.Name, line.Quantity, line.Zone));
            }
        }

        foreach (var item in created)
        {
            items.Add(item);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Gym {GymId} agregó {Count} equipos desde {Models} modelos del catálogo", gymId, created.Count, modelIds.Count);

        return await EquipmentResponses.BuildAsync(created, models, clock, cancellationToken);
    }

    private static EquipmentItem NewItem(Guid gymId, EquipmentModel model, string name, int quantity, string? zone)
        => EquipmentItem.Create(
            gymId, model.Id, name, model.Type, zone, null, null, model.DefaultInstructions, quantity, null, null);
}

/// <summary>
/// Cambia el estado de una máquina y deja la fila de historial en la misma transacción. Las reglas
/// (motivo obligatorio, mismo estado, baja, fecha pasada) las hace <see cref="EquipmentItem.ChangeStatus"/>.
/// </summary>
public sealed class ChangeEquipmentStatusHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IClock clock,
    IUnitOfWork unitOfWork,
    ILogger<ChangeEquipmentStatusHandler> logger)
{
    public async Task<EquipmentItemResponse> HandleAsync(
        Guid id, ChangeEquipmentStatusRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var item = await items.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Equipo no encontrado.");

        var change = item.ChangeStatus(
            request.Status, request.Reason, request.ExpectedBackOn, clock.Today, currentUser.UserId, clock.UtcNow);

        items.AddStatusChange(change);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Equipo {EquipmentId}: {From} -> {To}", item.Id, change.FromStatus, change.ToStatus);
        return await EquipmentResponses.BuildAsync(item, models, clock, cancellationToken);
    }
}

/// <summary>Historial de estado de una máquina, el más reciente primero, con el nombre de quien lo cambió.</summary>
public sealed class GetEquipmentHistoryHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    GSM.Application.Identity.Public.IIdentityReader identity)
{
    public async Task<IReadOnlyList<EquipmentStatusChangeResponse>> HandleAsync(
        Guid id, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        _ = await items.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Equipo no encontrado.");

        var changes = await items.ListStatusChangesAsync(id, cancellationToken);

        var names = new Dictionary<Guid, string?>();
        foreach (var userId in changes.Where(c => c.ChangedByUserId.HasValue).Select(c => c.ChangedByUserId!.Value).Distinct())
        {
            names[userId] = (await identity.GetUserAsync(userId, cancellationToken))?.FullName;
        }

        return changes
            .Select(c => new EquipmentStatusChangeResponse(
                c.Id,
                c.FromStatus.ToString(),
                c.ToStatus.ToString(),
                c.Reason,
                c.ExpectedBackOn,
                c.ChangedByUserId,
                c.ChangedByUserId is { } u ? names.GetValueOrDefault(u) : null,
                c.ChangedAt))
            .ToList();
    }
}

/// <summary>Baja lógica. Solo el Owner (lo impone la política del controller).</summary>
public sealed class DeactivateEquipmentHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    IUnitOfWork unitOfWork,
    ILogger<DeactivateEquipmentHandler> logger)
{
    public async Task HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var item = await items.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Equipo no encontrado.");

        item.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Equipo {EquipmentId} dado de baja", item.Id);
    }
}

/// <summary>
/// Foto de la máquina real, por <see cref="IFileStorage"/> (R2). Sin verificar con el servicio real:
/// R2 no está configurado en este entorno.
/// </summary>
public sealed class UploadEquipmentPhotoHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IClock clock,
    IFileStorage storage,
    IUnitOfWork unitOfWork)
{
    public async Task<EquipmentItemResponse> HandleAsync(
        Guid id, FileToStore file, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        ImageUploadRules.EnsureValid(file);

        var item = await items.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Equipo no encontrado.");

        var previous = item.PhotoUrl;
        var url = await storage.SaveAsync(file, "equipment", cancellationToken);

        item.SetPhoto(url);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previous))
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return await EquipmentResponses.BuildAsync(item, models, clock, cancellationToken);
    }
}
