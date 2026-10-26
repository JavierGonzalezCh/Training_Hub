using GSM.Application.Attendance.Public;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Events;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Attendance;

/// <summary>
/// T4: en un gym sin registro en la puerta y con el conteo «al registrar el primer ejercicio», el ejercicio registrado cuenta la
/// visita del día. Reacciona a <see cref="RoutineExerciseLogged"/> (cada ejercicio nuevo, no solo el primero del día), así
/// que si el primero se rechazó (membresía vencida) y el miembro renovó, el siguiente ejercicio del mismo día sí cuenta.
/// Es idempotente: si el día ya tiene entrada no hace nada, y el caso de uso lo es por día. Si el miembro no puede entrar, el
/// registro del ejercicio no se pierde: el rechazo queda como intento, igual que con el QR, y aquí solo se anota.
/// </summary>
public sealed class RoutineExerciseLoggedCountsTheDayHandler(
    IAttendanceReader attendanceReader,
    IMemberReader members,
    CheckInHandler checkIn,
    ILogger<RoutineExerciseLoggedCountsTheDayHandler> logger) : IDomainEventHandler<RoutineExerciseLogged>
{
    public async Task HandleAsync(RoutineExerciseLogged domainEvent, CancellationToken cancellationToken = default)
    {
        if (!await attendanceReader.IsFirstLogEntryAsync(domainEvent.GymId, cancellationToken)
            || await attendanceReader.GetFirstEntryTodayAsync(domainEvent.MemberId, cancellationToken) is not null)
        {
            return;
        }

        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        if (member is null)
        {
            return;
        }

        try
        {
            var (_, created) = await checkIn.HandleRoutineLogAsync(member, cancellationToken);
            logger.LogInformation(
                "Ejercicio del día de {MemberId}: entrada {Outcome}",
                domainEvent.MemberId, created ? "creada" : "ya existía");
        }
        catch (Exception ex) when (ex is ConflictException or ForbiddenDomainException)
        {
            logger.LogInformation(
                "Ejercicio de {MemberId}: el día no cuenta todavía ({Reason})", domainEvent.MemberId, ex.Message);
        }
    }
}
