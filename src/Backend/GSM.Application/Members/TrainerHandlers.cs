using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Identity.Events;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>
/// Quienes pueden ser entrenador asignado en el gym (para el selector de la ficha). Cualquier staff lo ve;
/// la lista la resuelve Identity.
/// </summary>
public sealed class ListTrainersHandler(ICurrentUserService currentUser, IIdentityReader identity)
{
    public async Task<IReadOnlyList<TrainerOption>> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var trainers = await identity.ListTrainersAsync(gymId, cancellationToken);
        return trainers.Select(t => new TrainerOption(t.UserId, t.FullName, t.IsOwner)).ToList();
    }
}

/// <summary>
/// Asigna, cambia o quita el entrenador de un miembro (M1b). Cualquier staff puede hacerlo: es un dato de la
/// ficha, una referencia y no un candado. El nuevo entrenador se valida contra Identity y el cambio queda en
/// el historial con los nombres (campo <c>trainer</c>).
/// </summary>
public sealed class AssignMemberTrainerHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    IIdentityReader identity,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    ILogger<AssignMemberTrainerHandler> logger)
{
    public const string HistoryField = "trainer";

    public async Task<MemberDetail> HandleAsync(
        Guid memberId, AssignTrainerRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        var previousId = member.TrainerUserId;
        if (request.TrainerUserId is { } trainerId
            && trainerId != previousId
            && !await identity.CanBeTrainerAsync(trainerId, gymId, cancellationToken))
        {
            throw new ValidationDomainException(
                "Esa persona no puede ser entrenador: debe ser el Owner o tener el permiso de entrenador en este gimnasio.");
        }

        if (member.AssignTrainer(request.TrainerUserId))
        {
            var oldName = await NameAsync(previousId, cancellationToken);
            var newName = await NameAsync(member.TrainerUserId, cancellationToken);
            changes.Record(member, [new MemberFieldChange(HistoryField, oldName, newName)]);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Entrenador del miembro {MemberId} cambiado a {TrainerUserId}", memberId, member.TrainerUserId);
        }

        var detail = MemberMapper.ToDetail(member, clock.Today);
        return detail with { TrainerName = await NameAsync(member.TrainerUserId, cancellationToken) };
    }

    private async Task<string?> NameAsync(Guid? userId, CancellationToken cancellationToken)
        => userId is { } id ? (await identity.GetUserAsync(id, cancellationToken))?.FullName : null;
}

/// <summary>
/// Si una cuenta se desactiva o pierde el permiso <c>Trainer</c>, sus miembros quedan sin entrenador y el
/// Owner los reasigna desde la lista "sin entrenador". Idempotente: sin miembros asignados no hace nada.
/// </summary>
public sealed class ClearTrainerOnRoleLostHandler(
    IMemberRepository members,
    IIdentityReader identity,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    ILogger<ClearTrainerOnRoleLostHandler> logger)
    : IDomainEventHandler<StaffTrainerRoleLost>
{
    public async Task HandleAsync(StaffTrainerRoleLost domainEvent, CancellationToken cancellationToken)
    {
        var assigned = (await members.GetByTrainerAsync(domainEvent.UserId, cancellationToken))
            .Where(m => m.GymId == domainEvent.GymId)
            .ToList();
        if (assigned.Count == 0)
        {
            return;
        }

        var oldName = (await identity.GetUserAsync(domainEvent.UserId, cancellationToken))?.FullName;
        foreach (var member in assigned)
        {
            member.AssignTrainer(null);
            changes.Record(member, [new MemberFieldChange(AssignMemberTrainerHandler.HistoryField, oldName, null)]);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "{Count} miembros quedaron sin entrenador al perder el rol la cuenta {UserId}",
            assigned.Count, domainEvent.UserId);
    }
}
