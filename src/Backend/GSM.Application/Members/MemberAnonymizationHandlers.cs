using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Application.Members.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>
/// Anonimiza una ficha (Members §11). Lo comparten la tarea periódica y la supresión a pedido del Owner. Corre con o sin
/// usuario: fija el gym explícito. Idempotente: una ficha ya anonimizada no hace nada. Nunca se loguean datos personales.
/// </summary>
public sealed class MemberAnonymizer(
    IClock clock,
    IMemberRepository members,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    IFileStorage storage,
    IDomainEventPublisher events,
    ILogger<MemberAnonymizer> logger)
{
    /// <returns><c>true</c> si esta llamada anonimizó la ficha.</returns>
    public async Task<bool> AnonymizeAsync(
        Guid gymId, Guid memberId, bool allowActive, CancellationToken cancellationToken)
    {
        var member = await members.GetForGymAsync(gymId, memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        if (member.IsAnonymized)
        {
            return false;
        }

        if (member.Status == MemberStatus.Active)
        {
            if (!allowActive)
            {
                throw new ConflictException("Solo se anonimiza a un miembro dado de baja.");
            }

            // Supresión a pedido: anonimizar implica la baja.
            member.Deactivate(clock.Today);
            changes.RecordStatus(member, MemberStatus.Active, MemberStatus.Inactive);
        }

        var userId = member.UserId;

        // Primero lo que cuelga de la ficha (idempotente, escribe al instante): si algo falla, la ficha sigue sin marcar
        // como anonimizada y la siguiente ejecución lo reintenta.
        await members.ClearPersonalRecordsAsync(gymId, memberId, cancellationToken);

        var photoUrl = member.Anonymize(clock.UtcNow);
        changes.Record(member, [new MemberFieldChange("anonymized", null, null)]);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(photoUrl))
        {
            try
            {
                await storage.DeleteAsync(photoUrl, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "No se pudo borrar la foto del miembro anonimizado {MemberId}", memberId);
            }
        }

        logger.LogInformation("Miembro {MemberId} anonimizado en el gym {GymId}", memberId, gymId);
        await events.PublishAsync(new MemberAnonymized(memberId, gymId, userId), cancellationToken);
        return true;
    }
}

/// <summary>Supresión a pedido (Ley 1581): el Owner anonimiza la ficha de inmediato, con confirmación.</summary>
public sealed class AnonymizeMemberHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    MemberAnonymizer anonymizer)
{
    public async Task<MemberDetail> HandleAsync(
        Guid memberId, AnonymizeMemberRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        if (!request.Confirm)
        {
            throw new ValidationDomainException("Confirma que quieres anonimizar la ficha: no se puede deshacer.");
        }

        _ = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        await anonymizer.AnonymizeAsync(gymId, memberId, allowActive: true, cancellationToken);

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");
        return MemberMapper.ToDetail(member, clock.Today);
    }
}

public sealed class GetMemberSettingsHandler(ICurrentUserService currentUser, IMemberRepository members)
{
    public async Task<MemberSettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await members.GetSettingsAsync(gymId, cancellationToken) ?? MemberSettings.Default(gymId);
        return new MemberSettingsResponse(current.AnonymizationMonths, MemberSettings.NoticeDays);
    }
}

/// <summary>Plazo de anonimización del gym. Sin fila vale el valor por defecto; la primera edición la crea.</summary>
public sealed class UpdateMemberSettingsHandler(
    ICurrentUserService currentUser,
    IMemberRepository members,
    IUnitOfWork unitOfWork,
    ILogger<UpdateMemberSettingsHandler> logger)
{
    public async Task<MemberSettingsResponse> HandleAsync(
        UpdateMemberSettingsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await members.GetSettingsAsync(gymId, cancellationToken);
        if (current is null)
        {
            // Validar antes de añadir: si el plazo no vale, nada queda en el contexto para un guardado posterior.
            current = MemberSettings.Default(gymId);
            current.Update(request.AnonymizationMonths);
            members.Add(current);
        }
        else
        {
            current.Update(request.AnonymizationMonths);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Plazo de anonimización del gym {GymId}: {Months} meses", gymId, current.AnonymizationMonths);
        return new MemberSettingsResponse(current.AnonymizationMonths, MemberSettings.NoticeDays);
    }
}

/// <summary>
/// Parte de la tarea periódica (M6): avisa a los Owners con <see cref="MemberSettings.NoticeDays"/> días de
/// anticipación y anonimiza las fichas cuyo plazo venció. Corre sin usuario (ve todos los gyms). Idempotente y tolera
/// ejecuciones perdidas: lo vencido se anonimiza en la siguiente. Una ficha que falla no frena a las demás.
/// </summary>
public sealed class RunAnonymizationJobHandler(
    IClock clock,
    IMemberRepository members,
    IUnitOfWork unitOfWork,
    MemberAnonymizer anonymizer,
    IDomainEventPublisher events,
    ILogger<RunAnonymizationJobHandler> logger)
{
    /// <summary>Tope por ejecución: el resto queda para la siguiente.</summary>
    public const int BatchSize = 200;

    /// <param name="onlyGymId">Solo para tests: limita la ejecución a un gym. En producción es nulo (todos).</param>
    public async Task<AnonymizationJobResult> HandleAsync(CancellationToken cancellationToken, Guid? onlyGymId = null)
    {
        var today = clock.Today;
        var monthsByGym = await members.GetAnonymizationMonthsByGymAsync(cancellationToken);

        // El plazo mínimo es el piso para acotar la consulta; el de cada gym se aplica en memoria.
        var floor = today.AddMonths(-MemberSettings.MinAnonymizationMonths).AddDays(MemberSettings.NoticeDays);
        var candidates = (await members.GetAnonymizationCandidatesAsync(floor, cancellationToken))
            .Where(c => onlyGymId is null || c.GymId == onlyGymId)
            .ToList();

        var notices = 0;
        var anonymized = 0;
        foreach (var candidate in candidates)
        {
            var months = monthsByGym.TryGetValue(candidate.GymId, out var m)
                ? m
                : MemberSettings.DefaultAnonymizationMonths;
            var anonymizesOn = candidate.DeactivatedOn.AddMonths(months);

            try
            {
                if (anonymizesOn <= today)
                {
                    if (anonymized < BatchSize
                        && await anonymizer.AnonymizeAsync(candidate.GymId, candidate.MemberId, false, cancellationToken))
                    {
                        anonymized++;
                    }
                }
                else if (anonymizesOn.AddDays(-MemberSettings.NoticeDays) <= today && !candidate.NoticeSent)
                {
                    var member = await members.GetForGymAsync(candidate.GymId, candidate.MemberId, cancellationToken);
                    if (member is null)
                    {
                        continue;
                    }

                    member.MarkAnonymizationNoticeSent(today);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    await events.PublishAsync(
                        new MemberAnonymizationDue(candidate.MemberId, candidate.GymId, anonymizesOn), cancellationToken);
                    notices++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex, "Falló la anonimización del miembro {MemberId} del gym {GymId}", candidate.MemberId, candidate.GymId);
            }
        }

        logger.LogInformation("Tarea de anonimización: {Notices} avisos y {Anonymized} fichas anonimizadas", notices, anonymized);
        return new AnonymizationJobResult(notices, anonymized);
    }
}
