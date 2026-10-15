using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Memberships;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

/// <summary>C3: las reglas de reserva que se comprueban antes de reservar o cancelar.</summary>
internal static class ClassBookingRules
{
    public static async Task<ClassSettings> LoadAsync(
        IClassSettingsRepository settings, Guid gymId, CancellationToken cancellationToken)
        => await settings.GetAsync(gymId, cancellationToken) ?? ClassSettings.Default(gymId);

    /// <summary>
    /// Penalización vigente, ventana de reserva y máximo de reservas activas. El bloqueo y el máximo son 409
    /// (choque de estado); la ventana es 422 (la fecha no cumple la regla). Va antes de materializar la sesión.
    /// </summary>
    public static async Task EnsureCanBookAsync(
        IClock clock,
        IBookingBlockRepository blocks,
        IClassBookingRepository bookings,
        ClassSettings rules,
        Guid memberId,
        DateOnly sessionDate,
        CancellationToken cancellationToken)
    {
        var today = clock.Today;

        var block = await blocks.GetActiveAsync(memberId, today, cancellationToken);
        if (block is not null)
        {
            throw new ConflictException(
                $"No puedes reservar hasta el {CommercialStatusCalculator.ShortDate(block.BlockedUntil)} por inasistencias.");
        }

        if (sessionDate > today.AddDays(rules.BookingWindowDays))
        {
            throw new ValidationDomainException(
                $"Las reservas abren {rules.BookingWindowDays} días antes de la clase; "
                + $"podrás reservar desde el {CommercialStatusCalculator.ShortDate(sessionDate.AddDays(-rules.BookingWindowDays))}.");
        }

        var active = await CountActiveAsync(clock, bookings, memberId, cancellationToken);
        if (active >= rules.MaxActiveBookings)
        {
            throw new ConflictException(
                $"Ya tienes {active} reservas activas (el máximo es {rules.MaxActiveBookings}); cancela una para reservar otra.");
        }
    }

    public static Task<int> CountActiveAsync(
        IClock clock, IClassBookingRepository bookings, Guid memberId, CancellationToken cancellationToken)
    {
        var nowLocal = clock.ToBusinessLocal(clock.UtcNow);
        return bookings.CountFutureActiveForMemberAsync(
            memberId, DateOnly.FromDateTime(nowLocal), TimeOnly.FromDateTime(nowLocal), cancellationToken);
    }

    /// <summary>Cancelar pasado el plazo (hora de inicio menos <c>CancelDeadlineHours</c>) cuenta como inasistencia.</summary>
    public static bool IsLateCancellation(IClock clock, ClassSettings rules, ClassSession session)
    {
        var startsLocal = session.SessionDate.ToDateTime(session.StartTime);
        return clock.ToBusinessLocal(clock.UtcNow) > startsLocal.AddHours(-rules.CancelDeadlineHours);
    }
}

/// <summary>
/// C3: tras una inasistencia (marcada por el staff o cancelación tardía) revisa si el miembro alcanzó el
/// límite de la ventana y, si es así, lo bloquea. Se llama <b>después</b> de guardar la reserva con su
/// <c>StrikeAt</c>. Las inasistencias se cuentan desde el último bloqueo (o desde el inicio de la ventana,
/// lo más reciente), así que levantar o cumplir una penalización empieza el conteo de cero.
/// </summary>
public sealed class BookingPenaltyService(
    IClock clock,
    IClassSettingsRepository settings,
    IClassBookingRepository bookings,
    IBookingBlockRepository blocks,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<BookingPenaltyService> logger)
{
    /// <returns>El bloqueo recién aplicado, o <c>null</c> si no se alcanzó el límite o ya había uno vigente.</returns>
    public async Task<BookingBlock?> EvaluateAsync(Guid gymId, Guid memberId, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        if (await blocks.GetActiveAsync(memberId, today, cancellationToken) is not null)
        {
            return null;
        }

        var rules = await ClassBookingRules.LoadAsync(settings, gymId, cancellationToken);

        var since = clock.UtcNow.AddDays(-rules.NoShowWindowDays);
        if (await blocks.GetLastAppliedAtAsync(memberId, cancellationToken) is { } lastBlock && lastBlock > since)
        {
            since = lastBlock;
        }

        var strikes = await bookings.CountStrikesSinceAsync(memberId, since, cancellationToken);
        if (strikes < rules.NoShowLimit)
        {
            return null;
        }

        // El bloqueo cubre NoShowBlockDays días contando hoy: "hasta" es el último día sin poder reservar.
        var block = BookingBlock.Apply(gymId, memberId, today.AddDays(rules.NoShowBlockDays - 1), strikes, clock.UtcNow);
        blocks.Add(block);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Miembro {MemberId} bloqueado para reservar hasta {BlockedUntil} ({Strikes} inasistencias)",
            memberId, block.BlockedUntil, strikes);

        // Notifications consumirá este evento para avisar al miembro (no hay escritura cruzada).
        await events.PublishAsync(new BookingPenaltyApplied(memberId, gymId, block.BlockedUntil), cancellationToken);
        return block;
    }
}
