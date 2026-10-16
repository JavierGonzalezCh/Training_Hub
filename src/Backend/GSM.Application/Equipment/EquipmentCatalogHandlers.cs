using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Equipment.Abstractions;
using GSM.Application.Equipment.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Equipment;

/// <summary>
/// S4: administración del catálogo de máquinas del sistema (<c>equipment.equipment_models</c>). Solo el SuperAdmin;
/// los gyms siguen leyendo el catálogo por <c>GET /api/equipment/models</c> (solo los activos).
/// </summary>
internal static class EquipmentCatalogAccess
{
    public static void EnsureSuperAdmin(ICurrentUserService currentUser)
    {
        if (!currentUser.IsSuperAdmin)
        {
            throw new ForbiddenDomainException("Solo un superadmin administra el catálogo de máquinas del sistema.");
        }
    }

    public static AdminEquipmentModelResponse ToAdminResponse(EquipmentModel m)
        => new(m.Id, m.Name, m.Type.ToString(), m.ImageUrl, m.Description, m.DefaultInstructions,
            m.TrackedPerUnit, m.IsActive);
}

public sealed class ListAdminEquipmentModelsHandler(ICurrentUserService currentUser, IEquipmentModelRepository models)
{
    public async Task<IReadOnlyList<AdminEquipmentModelResponse>> HandleAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        EquipmentCatalogAccess.EnsureSuperAdmin(currentUser);
        var list = await models.ListAllAsync(includeInactive, cancellationToken);
        return list.Select(EquipmentCatalogAccess.ToAdminResponse).ToList();
    }
}

public sealed class CreateEquipmentModelHandler(
    ICurrentUserService currentUser,
    IEquipmentModelRepository models,
    IUnitOfWork unitOfWork,
    ILogger<CreateEquipmentModelHandler> logger)
{
    public async Task<AdminEquipmentModelResponse> HandleAsync(
        SaveEquipmentModelRequest request, CancellationToken cancellationToken)
    {
        EquipmentCatalogAccess.EnsureSuperAdmin(currentUser);

        var name = request.Name.Trim();
        if (await models.NameTakenAsync(name, null, cancellationToken))
        {
            throw new ConflictException($"Ya existe un modelo con el nombre '{name}'.");
        }

        var model = EquipmentModel.Create(
            name, request.Type, request.Description, request.DefaultInstructions, request.TrackedPerUnit);
        models.Add(model);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("SuperAdmin creó el modelo de máquina {ModelId} ({Name})", model.Id, model.Name);
        return EquipmentCatalogAccess.ToAdminResponse(model);
    }
}

public sealed class UpdateEquipmentModelHandler(
    ICurrentUserService currentUser, IEquipmentModelRepository models, IUnitOfWork unitOfWork)
{
    public async Task<AdminEquipmentModelResponse> HandleAsync(
        Guid id, SaveEquipmentModelRequest request, CancellationToken cancellationToken)
    {
        EquipmentCatalogAccess.EnsureSuperAdmin(currentUser);

        var model = await models.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Modelo no encontrado.");

        var name = request.Name.Trim();
        if (await models.NameTakenAsync(name, id, cancellationToken))
        {
            throw new ConflictException($"Ya existe otro modelo con el nombre '{name}'.");
        }

        model.Update(name, request.Type, request.Description, request.DefaultInstructions, request.TrackedPerUnit);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EquipmentCatalogAccess.ToAdminResponse(model);
    }
}

/// <summary>Activa o desactiva un modelo. Idempotente. Desactivar no toca las máquinas de los gyms ni las ligas.</summary>
public sealed class SetEquipmentModelActiveHandler(
    ICurrentUserService currentUser, IEquipmentModelRepository models, IUnitOfWork unitOfWork)
{
    public async Task<AdminEquipmentModelResponse> HandleAsync(
        Guid id, bool active, CancellationToken cancellationToken)
    {
        EquipmentCatalogAccess.EnsureSuperAdmin(currentUser);

        var model = await models.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Modelo no encontrado.");

        if (model.IsActive != active)
        {
            if (active)
            {
                model.Activate();
            }
            else
            {
                model.Deactivate();
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return EquipmentCatalogAccess.ToAdminResponse(model);
    }
}

/// <summary>Imagen de referencia del modelo (JPEG, PNG o WEBP, hasta 5 MB). Reemplaza y borra la anterior.</summary>
public sealed class UploadEquipmentModelImageHandler(
    ICurrentUserService currentUser,
    IEquipmentModelRepository models,
    IFileStorage storage,
    IUnitOfWork unitOfWork)
{
    public async Task<AdminEquipmentModelResponse> HandleAsync(
        Guid id, FileToStore file, CancellationToken cancellationToken)
    {
        EquipmentCatalogAccess.EnsureSuperAdmin(currentUser);
        ImageUploadRules.EnsureValid(file);

        var model = await models.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Modelo no encontrado.");

        var previous = model.ImageUrl;
        var url = await storage.SaveAsync(file, "equipment-models", cancellationToken);

        model.SetImage(url);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previous))
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return EquipmentCatalogAccess.ToAdminResponse(model);
    }
}
