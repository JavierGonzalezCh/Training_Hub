using GSM.Application.Classes.Abstractions;
using GSM.Application.Common.Abstractions;
using GSM.Domain.Classes;

namespace GSM.Application.Classes;

/// <summary>
/// Materialización perezosa de una ocurrencia: crea la fila <see cref="ClassSession"/> copiando la
/// definición solo cuando hace falta (una cancelación, una reserva, una marca de asistencia).
/// El índice único <c>(class_id, session_date)</c> la hace idempotente frente a la concurrencia.
/// </summary>
internal static class ClassSessionMaterializer
{
    public static async Task<ClassSession> GetOrCreateAsync(
        IClassSessionRepository sessions,
        IUnitOfWork unitOfWork,
        GymClass gymClass,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var existing = await sessions.GetAsync(gymClass.Id, date, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var session = ClassSession.Materialize(
            gymClass.GymId, gymClass.Id, date,
            gymClass.StartTime, gymClass.DurationMinutes, gymClass.InstructorName, gymClass.Capacity);

        sessions.Add(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }
}
