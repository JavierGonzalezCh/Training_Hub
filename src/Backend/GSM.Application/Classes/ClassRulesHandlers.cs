using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

internal static class ClassSettingsMapper
{
    public static ClassSettingsResponse ToResponse(ClassSettings s)
        => new(s.CancelDeadlineHours, s.NoShowLimit, s.NoShowWindowDays, s.NoShowBlockDays, s.BookingWindowDays, s.MaxActiveBookings);
}

/// <summary>Reglas de reserva del gym (C3). Sin fila devuelve los valores por defecto.</summary>
public sealed class GetClassSettingsHandler(ICurrentUserService currentUser, IClassSettingsRepository settings)
{
    public async Task<ClassSettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        return ClassSettingsMapper.ToResponse(await ClassBookingRules.LoadAsync(settings, gymId, cancellationToken));
    }
}

/// <summary>Cambia las reglas de reserva. La primera edición crea la fila; los cambios rigen hacia adelante.</summary>
public sealed class UpdateClassSettingsHandler(
    ICurrentUserService currentUser,
    IClassSettingsRepository settings,
    IUnitOfWork unitOfWork,
    ILogger<UpdateClassSettingsHandler> logger)
{
    public async Task<ClassSettingsResponse> HandleAsync(
        UpdateClassSettingsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await settings.GetAsync(gymId, cancellationToken);
        if (current is null)
        {
            current = ClassSettings.Default(gymId);
            settings.Add(current);
        }

        current.Update(
            request.CancelDeadlineHours, request.NoShowLimit, request.NoShowWindowDays,
            request.NoShowBlockDays, request.BookingWindowDays, request.MaxActiveBookings);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Reglas de reserva del gym {GymId} actualizadas", gymId);
        return ClassSettingsMapper.ToResponse(current);
    }
}

/// <summary>Estado de reservas del miembro: bloqueo vigente, reservas activas y las reglas que se le advierten.</summary>
public sealed class GetMyBookingStatusHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IClassSettingsRepository settings,
    IClassBookingRepository bookings,
    IBookingBlockRepository blocks)
{
    public async Task<MyBookingStatusResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var rules = await ClassBookingRules.LoadAsync(settings, gymId, cancellationToken);
        var block = await blocks.GetActiveAsync(self.Id, clock.Today, cancellationToken);
        var active = await ClassBookingRules.CountActiveAsync(clock, bookings, self.Id, cancellationToken);

        return new MyBookingStatusResponse(
            block?.BlockedUntil, active, rules.MaxActiveBookings, rules.CancelDeadlineHours, rules.BookingWindowDays);
    }
}

/// <summary>Miembros con la penalización de reservas vigente (staff).</summary>
public sealed class ListBookingBlocksHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IBookingBlockRepository blocks)
{
    public async Task<IReadOnlyList<BookingBlockResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var rows = await blocks.ListActiveAsync(clock.Today, cancellationToken);
        var result = new List<BookingBlockResponse>();
        foreach (var b in rows)
        {
            var member = await members.GetAsync(b.MemberId, cancellationToken);
            result.Add(new BookingBlockResponse(
                b.Id, b.MemberId,
                member is null ? "(miembro eliminado)" : $"{member.FirstName} {member.LastName}",
                b.BlockedUntil, b.StrikeCount, b.AppliedAt));
        }

        return result;
    }
}

/// <summary>El staff levanta la penalización de un miembro, con motivo obligatorio.</summary>
public sealed class LiftBookingBlockHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IBookingBlockRepository blocks,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<LiftBookingBlockHandler> logger)
{
    public async Task HandleAsync(Guid blockId, LiftBookingBlockRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var block = await blocks.GetAsync(blockId, cancellationToken)
            ?? throw new NotFoundException("Penalización no encontrada.");

        block.Lift(currentUser.UserId, request.Reason, clock.UtcNow, clock.Today);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Penalización {BlockId} del miembro {MemberId} levantada", block.Id, block.MemberId);
        await events.PublishAsync(new BookingPenaltyLifted(block.MemberId, gymId), cancellationToken);
    }
}
