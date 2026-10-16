using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Equipment.Abstractions;
using GSM.Application.Equipment.Contracts;
using GSM.Application.Equipment.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Equipment;

/// <summary>Mapeo manual de los reportes: los nombres salen de Identity, la máquina del inventario.</summary>
internal static class EquipmentIssueResponses
{
    public const int MaxListed = 200;

    public static async Task<IReadOnlyList<EquipmentIssueResponse>> BuildAsync(
        IReadOnlyList<EquipmentIssue> issues,
        IEquipmentItemRepository items,
        IEquipmentModelRepository models,
        IIdentityReader identity,
        CancellationToken cancellationToken)
    {
        var itemMap = await items.GetByIdsAsync(issues.Select(i => i.ItemId).Distinct().ToList(), cancellationToken);
        var images = (await models.GetByIdsAsync(
                itemMap.Values.Where(i => i.ModelId.HasValue).Select(i => i.ModelId!.Value).Distinct().ToList(),
                cancellationToken))
            .ToDictionary(m => m.Id, m => m.ImageUrl);

        var names = new Dictionary<Guid, string?>();
        var userIds = issues.Where(i => i.ReportedByUserId.HasValue).Select(i => i.ReportedByUserId!.Value)
            .Concat(issues.Where(i => i.ResolvedByUserId.HasValue).Select(i => i.ResolvedByUserId!.Value))
            .Distinct();
        foreach (var userId in userIds)
        {
            names[userId] = (await identity.GetUserAsync(userId, cancellationToken))?.FullName;
        }

        return issues.Select(i =>
        {
            itemMap.TryGetValue(i.ItemId, out var item);
            var photo = item?.PhotoUrl ?? (item?.ModelId is { } m ? images.GetValueOrDefault(m) : null);
            return new EquipmentIssueResponse(
                i.Id,
                i.ItemId,
                item?.Name ?? "Equipo",
                item?.Zone,
                photo,
                i.Reason.ToString(),
                i.Description,
                i.PhotoUrl,
                i.Status.ToString(),
                i.IsUrgent,
                i.SeenAt is not null,
                i.ReportedByRole.ToString(),
                i.ReportedByUserId is { } by ? names.GetValueOrDefault(by) : null,
                i.CreatedAt,
                i.ResolvedAt,
                i.ResolvedByUserId is { } res ? names.GetValueOrDefault(res) : null,
                i.Resolution);
        }).ToList();
    }

    public static MyEquipmentIssueResponse ToMine(EquipmentIssue issue, string itemName)
        => new(
            issue.Id,
            issue.ItemId,
            itemName,
            issue.Reason.ToString(),
            issue.Description,
            issue.Status.ToString(),
            issue.CreatedAt,
            issue.ResolvedAt,
            issue.Status == EquipmentIssueStatus.Resolved ? issue.Resolution : null);
}

/// <summary>Núcleo común de «reportar una falla»: lo usan el miembro y el staff.</summary>
internal static class EquipmentIssueReporting
{
    public static async Task<EquipmentIssue> ReportAsync(
        Guid gymId,
        Guid itemId,
        Guid userId,
        IssueReporterRole role,
        ReportEquipmentIssueRequest request,
        IEquipmentItemRepository items,
        IEquipmentIssueRepository issues,
        IClock clock,
        IUnitOfWork unitOfWork,
        IDomainEventPublisher events,
        CancellationToken cancellationToken)
    {
        var item = await items.GetAsync(itemId, cancellationToken)
            ?? throw new NotFoundException("Equipo no encontrado.");
        if (!item.IsActive)
        {
            throw new ConflictException("El equipo está dado de baja y no admite reportes.");
        }

        // Un miembro tiene como máximo un reporte sin cerrar por máquina (Equipment §4).
        if (role == IssueReporterRole.Member
            && await issues.FindUnclosedByReporterAsync(itemId, userId, cancellationToken) is not null)
        {
            throw new ConflictException("Ya reportaste esta máquina. Te avisamos cuando se resuelva.");
        }

        var others = (await issues.ListByItemAsync(itemId, cancellationToken))
            .Where(i => !i.IsClosed).Select(i => i.Id).ToList();

        var issue = EquipmentIssue.Report(gymId, itemId, userId, role, request.Reason, request.Description);
        issues.Add(issue);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await events.PublishAsync(
            new EquipmentIssueReported(
                gymId, issue.Id, itemId, item.Name, issue.Reason, issue.Description, userId, role, others, clock.UtcNow),
            cancellationToken);
        return issue;
    }
}

/// <summary>
/// El staff reporta una falla desde el panel (p. ej. la vio al pasar). Mismo flujo que el del miembro, sin el
/// flag del gym ni el límite de un reporte abierto.
/// </summary>
public sealed class ReportEquipmentIssueByStaffHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    IEquipmentIssueRepository issues,
    IEquipmentModelRepository models,
    IIdentityReader identity,
    IClock clock,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<ReportEquipmentIssueByStaffHandler> logger)
{
    public async Task<EquipmentIssueResponse> HandleAsync(
        Guid itemId, ReportEquipmentIssueRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        var issue = await EquipmentIssueReporting.ReportAsync(
            gymId, itemId, userId, IssueReporterRole.Staff, request, items, issues, clock, unitOfWork, events,
            cancellationToken);

        logger.LogInformation("Reporte {IssueId} de falla del equipo {EquipmentId} creado por el staff", issue.Id, itemId);
        return (await EquipmentIssueResponses.BuildAsync([issue], items, models, identity, cancellationToken))[0];
    }
}

/// <summary>El miembro reporta una falla desde la app. 403 si el gym apagó <c>MembersCanReportEquipment</c>.</summary>
public sealed class ReportEquipmentIssueByMemberHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IMemberReader members,
    IEquipmentItemRepository items,
    IEquipmentIssueRepository issues,
    IClock clock,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<ReportEquipmentIssueByMemberHandler> logger)
{
    public async Task<MyEquipmentIssueResponse> HandleAsync(
        Guid itemId, ReportEquipmentIssueRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
        await EquipmentFeatureGate.EnsureMemberReportsEnabledAsync(gyms, gymId, cancellationToken);

        _ = await members.ResolveByUserIdAsync(userId, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var issue = await EquipmentIssueReporting.ReportAsync(
            gymId, itemId, userId, IssueReporterRole.Member, request, items, issues, clock, unitOfWork, events,
            cancellationToken);

        var item = await items.GetAsync(itemId, cancellationToken);
        logger.LogInformation("Reporte {IssueId} de falla del equipo {EquipmentId} creado por un miembro", issue.Id, itemId);
        return EquipmentIssueResponses.ToMine(issue, item?.Name ?? "Equipo");
    }
}

/// <summary><c>members_can_report_equipment</c>: sin él, el miembro recibe 403 al reportar.</summary>
internal static class EquipmentFeatureGate
{
    public static async Task EnsureMemberReportsEnabledAsync(
        IGymReader gyms, Guid gymId, CancellationToken cancellationToken)
    {
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
        if (flags is null || !flags.MembersCanReportEquipment)
        {
            throw new ForbiddenDomainException("Tu gimnasio no permite reportar fallas desde la app.");
        }
    }
}

/// <summary>
/// Foto del reporte, por <see cref="IFileStorage"/> (R2). La sube quien hizo el reporte, mientras siga sin
/// cerrar. Sin verificar con el servicio real: R2 no está configurado en este entorno.
/// </summary>
public sealed class UploadEquipmentIssuePhotoHandler(
    ICurrentUserService currentUser,
    IEquipmentIssueRepository issues,
    IFileStorage storage,
    IUnitOfWork unitOfWork)
{
    public async Task<MyEquipmentIssueResponse> HandleAsync(
        Guid issueId, FileToStore file, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
        ImageUploadRules.EnsureValid(file);

        // El reporte de otra persona se trata como inexistente.
        var issue = await issues.GetAsync(issueId, cancellationToken);
        if (issue is null || issue.ReportedByUserId != userId)
        {
            throw new NotFoundException("Reporte no encontrado.");
        }

        if (issue.IsClosed)
        {
            throw new ConflictException("El reporte ya está cerrado.");
        }

        var previous = issue.PhotoUrl;
        var url = await storage.SaveAsync(file, "equipment-issues", cancellationToken);
        issue.SetPhoto(url);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previous))
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return EquipmentIssueResponses.ToMine(issue, "Equipo");
    }
}

/// <summary>Reportes del gym para el staff: urgentes y abiertos primero.</summary>
public sealed class ListEquipmentIssuesHandler(
    ICurrentUserService currentUser,
    IEquipmentIssueRepository issues,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IIdentityReader identity)
{
    public async Task<IReadOnlyList<EquipmentIssueResponse>> HandleAsync(
        EquipmentIssueFilter filter, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var list = await issues.ListAsync(filter, EquipmentIssueResponses.MaxListed, cancellationToken);
        return await EquipmentIssueResponses.BuildAsync(list, items, models, identity, cancellationToken);
    }
}

/// <summary>
/// Un reporte. Abrirlo por primera vez lo marca como visto y, si ya no queda otro de la máquina sin ver, atiende
/// el aviso del staff (lo hace Notifications al recibir <see cref="EquipmentIssueSeen"/>). Una consulta de un
/// SuperAdmin impersonando no marca nada: el gym no debe enterarse.
/// </summary>
public sealed class GetEquipmentIssueHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IEquipmentIssueRepository issues,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IIdentityReader identity,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events)
{
    public async Task<EquipmentIssueResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var issue = await issues.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Reporte no encontrado.");

        if (!currentUser.IsImpersonating
            && currentUser.UserId is { } userId
            && issue.MarkSeen(userId, clock.UtcNow))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var all = await issues.ListByItemAsync(issue.ItemId, cancellationToken);
            await events.PublishAsync(
                new EquipmentIssueSeen(
                    gymId, issue.Id, issue.ItemId, userId,
                    AllOpenSeen: all.All(i => i.IsClosed || i.SeenAt is not null),
                    all.Select(i => i.Id).ToList(), clock.UtcNow),
                cancellationToken);
        }

        return (await EquipmentIssueResponses.BuildAsync([issue], items, models, identity, cancellationToken))[0];
    }
}

/// <summary>
/// Cambia el estado de un reporte (en revisión, resuelto, descartado). No toca el estado de la máquina: quien
/// atiende lo decide por separado (<c>POST /api/equipment/{id}/status</c>).
/// </summary>
public sealed class UpdateEquipmentIssueHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IEquipmentIssueRepository issues,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IIdentityReader identity,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<UpdateEquipmentIssueHandler> logger)
{
    public async Task<EquipmentIssueResponse> HandleAsync(
        Guid id, UpdateEquipmentIssueRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        var issue = await issues.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Reporte no encontrado.");

        var now = clock.UtcNow;
        var wasSeen = issue.SeenAt is not null;
        issue.ChangeStatus(request.Status, request.Resolution, userId, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Reporte {IssueId} pasó a {Status}", issue.Id, issue.Status);

        if (!issue.IsClosed && !wasSeen)
        {
            // Pasarlo a «en revisión» es abrirlo: deja de estar sin ver.
            var all = await issues.ListByItemAsync(issue.ItemId, cancellationToken);
            await events.PublishAsync(
                new EquipmentIssueSeen(
                    gymId, issue.Id, issue.ItemId, userId,
                    AllOpenSeen: all.All(i => i.IsClosed || i.SeenAt is not null),
                    all.Select(i => i.Id).ToList(), now),
                cancellationToken);
        }

        if (issue.IsClosed)
        {
            var item = (await items.GetByIdsAsync([issue.ItemId], cancellationToken)).GetValueOrDefault(issue.ItemId);
            var all = await issues.ListByItemAsync(issue.ItemId, cancellationToken);
            await events.PublishAsync(
                new EquipmentIssueClosed(
                    gymId, issue.Id, issue.ItemId, item?.Name ?? "Equipo",
                    Dismissed: issue.Status == EquipmentIssueStatus.Dismissed,
                    issue.ReportedByUserId, issue.ReportedByRole, userId,
                    OpenIssuesLeft: all.Count(i => !i.IsClosed),
                    all.Select(i => i.Id).ToList(), now),
                cancellationToken);
        }

        return (await EquipmentIssueResponses.BuildAsync([issue], items, models, identity, cancellationToken))[0];
    }
}

/// <summary>
/// Cuando el staff atiende a mano el aviso de la campana, los reportes abiertos de esa máquina quedan vistos
/// (igual que el intento de entrada rechazado). Idempotente.
/// </summary>
public sealed class EquipmentIssueNoticeResolvedMarksSeen(
    IEquipmentIssueRepository issues,
    IClock clock,
    IUnitOfWork unitOfWork) : IDomainEventHandler<ActionNoticeResolved>
{
    public async Task HandleAsync(ActionNoticeResolved domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent.Kind != ActionNoticeKinds.EquipmentIssue)
        {
            return;
        }

        var head = await issues.GetAsync(domainEvent.ReferenceId, cancellationToken);
        if (head is null)
        {
            return;
        }

        var changed = false;
        foreach (var issue in await issues.ListByItemAsync(head.ItemId, cancellationToken))
        {
            if (!issue.IsClosed && issue.MarkSeen(domainEvent.ResolvedBy, clock.UtcNow))
            {
                changed = true;
            }
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

// ---- lado del miembro ----

/// <summary>Una máquina vista por el miembro: foto, zona, instrucciones, estado y su reporte sin cerrar.</summary>
public sealed class GetMyEquipmentHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IEquipmentItemRepository items,
    IEquipmentModelRepository models,
    IEquipmentIssueRepository issues)
{
    public async Task<MyEquipmentResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        var item = await items.GetAsync(id, cancellationToken);
        if (item is null || !item.IsActive)
        {
            throw new NotFoundException("Equipo no encontrado.");
        }

        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);

        string? modelImage = null;
        if (item.ModelId is { } modelId)
        {
            modelImage = (await models.GetByIdsAsync([modelId], cancellationToken)).FirstOrDefault()?.ImageUrl;
        }

        var open = await issues.FindUnclosedByReporterAsync(id, userId, cancellationToken);
        return new MyEquipmentResponse(
            item.Id,
            item.Name,
            item.Type.ToString(),
            item.PhotoUrl ?? modelImage,
            item.Zone,
            item.Instructions,
            item.Status.ToString(),
            item.ExpectedBackOn,
            flags?.MembersCanReportEquipment == true,
            open is null ? null : EquipmentIssueResponses.ToMine(open, item.Name));
    }
}

/// <summary>Los reportes del miembro y en qué van.</summary>
public sealed class ListMyEquipmentIssuesHandler(
    ICurrentUserService currentUser,
    IEquipmentIssueRepository issues,
    IEquipmentItemRepository items)
{
    public async Task<IReadOnlyList<MyEquipmentIssueResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        var mine = await issues.ListByReporterAsync(userId, EquipmentIssueResponses.MaxListed, cancellationToken);
        var itemMap = await items.GetByIdsAsync(mine.Select(i => i.ItemId).Distinct().ToList(), cancellationToken);
        return mine
            .Select(i => EquipmentIssueResponses.ToMine(i, itemMap.TryGetValue(i.ItemId, out var item) ? item.Name : "Equipo"))
            .ToList();
    }
}

/// <summary>Tarjeta del dashboard: máquinas fuera de servicio o en mantenimiento, y reportes abiertos.</summary>
public sealed class GetEquipmentSummaryHandler(
    ICurrentUserService currentUser,
    IEquipmentItemRepository items,
    IEquipmentIssueRepository issues)
{
    public async Task<EquipmentSummaryResponse> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var byStatus = await items.CountActiveByStatusAsync(cancellationToken);
        var (open, urgent) = await issues.CountUnclosedAsync(cancellationToken);
        return new EquipmentSummaryResponse(
            byStatus.GetValueOrDefault(EquipmentStatus.OutOfService),
            byStatus.GetValueOrDefault(EquipmentStatus.Maintenance),
            open,
            urgent);
    }
}
