using GSM.Application.Common.Abstractions;
using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>Qué pasó al programar un aviso.</summary>
public enum ScheduleOutcome
{
    /// <summary>No existía: se creó con su hora.</summary>
    Created,

    /// <summary>Ya existía (misma clave de idempotencia) y se movió a la hora pedida, reactivándolo si estaba cancelado.</summary>
    Rescheduled,

    /// <summary>Ya existía y no admite cambios (ya salió, falló o se omitió).</summary>
    Untouched,
}

/// <summary>
/// N5: el mecanismo de avisos programados y cancelables (spec Notifications §4.4). Un aviso programado es un
/// <see cref="Notification"/> normal con <c>ScheduledFor</c> en el futuro: no aparece en la bandeja ni lo toma
/// el dispatcher hasta su hora, y si el hecho que lo originó se deshace, pasa a <c>cancelled</c>. Lo usan los
/// manejadores de eventos de Notifications (recordatorio de clase, cumpleaños, vencimientos); es la pieza que
/// CO5 (cumpleaños) aprovecha publicando <c>BirthdayGreetingDue</c>.
/// <para>
/// Es idempotente por <c>(tipo, destinatario, referencia)</c>: repetir el evento no duplica; volver a programar
/// un aviso cancelado lo reactiva (reservar, cancelar y reservar de nuevo la misma sesión). No guarda: el
/// llamante decide cuándo hacer <c>SaveChanges</c>.
/// </para>
/// </summary>
public sealed class ScheduledNoticeService(
    INotificationRepository notifications,
    MemberNoticeWriter writer,
    IClock clock)
{
    /// <summary>Cuánto antes del inicio sale el recordatorio de clase (spec §4.4: 10 minutos).</summary>
    public static readonly TimeSpan ClassReminderLead = TimeSpan.FromMinutes(10);

    /// <summary>Hora local del gym a la que salen los avisos de vencimiento y los cumpleaños (spec §9).</summary>
    public static readonly TimeOnly DailySendTime = new(8, 0);

    /// <summary>El instante UTC de una hora local del gym en un día de negocio.</summary>
    public DateTimeOffset At(DateOnly day, TimeOnly time)
        => clock.BusinessDayRange(day).Start + time.ToTimeSpan();

    /// <summary>
    /// Para los avisos "de las 8:00": si aún no son las 8:00 locales, hoy a las 8:00; si ya pasaron, ahora.
    /// Nunca en el pasado.
    /// </summary>
    public DateTimeOffset NextDailySend()
    {
        var now = clock.UtcNow;
        var send = At(clock.Today, DailySendTime);
        return now < send ? send : now;
    }

    /// <summary>Programa (o reprograma) un aviso a un miembro. <paramref name="notice"/> debe traer <c>ScheduledFor</c>.</summary>
    public async Task<ScheduleOutcome> ScheduleForMemberAsync(
        MemberNotice notice, bool sendEmail, CancellationToken cancellationToken)
    {
        var scheduledFor = notice.ScheduledFor
            ?? throw new ArgumentException("Un aviso programado necesita su hora.", nameof(notice));

        var existing = await notifications.FindAsync(
            notice.Type, null, notice.MemberId, notice.ReferenceId, cancellationToken);
        if (existing is null)
        {
            await writer.NotifyAsync(notice, sendEmail, cancellationToken);
            return ScheduleOutcome.Created;
        }

        return existing.Reschedule(scheduledFor) ? ScheduleOutcome.Rescheduled : ScheduleOutcome.Untouched;
    }

    /// <summary>Programa (o reprograma) un aviso informativo a una persona del equipo.</summary>
    public async Task<ScheduleOutcome> ScheduleForUserAsync(
        Guid gymId, Guid userId, NotificationType type, Guid referenceId,
        string title, string body, string? link, DateTimeOffset scheduledFor,
        CancellationToken cancellationToken)
    {
        var existing = await notifications.FindAsync(type, userId, null, referenceId, cancellationToken);
        if (existing is null)
        {
            notifications.Add(Notification.CreateForUser(
                gymId, userId, type, referenceId, title, Truncate(body), link, scheduledFor));
            return ScheduleOutcome.Created;
        }

        return existing.Reschedule(scheduledFor) ? ScheduleOutcome.Rescheduled : ScheduleOutcome.Untouched;
    }

    /// <summary>Cancela el aviso de un miembro si aún no salió. <c>false</c> si no existía o ya había salido.</summary>
    public async Task<bool> CancelForMemberAsync(
        NotificationType type, Guid memberId, Guid referenceId, CancellationToken cancellationToken)
    {
        var existing = await notifications.FindAsync(type, null, memberId, referenceId, cancellationToken);
        if (existing is null || existing.Status is not (NotificationStatus.Pending or NotificationStatus.Held))
        {
            return false;
        }

        existing.Cancel();
        return true;
    }

    /// <summary>Cancela los avisos sin salir de esos tipos y esa referencia, de cualquier destinatario.</summary>
    /// <returns>Cuántos canceló.</returns>
    public async Task<int> CancelAllByReferenceAsync(
        IReadOnlyCollection<NotificationType> types, Guid referenceId, CancellationToken cancellationToken)
    {
        var unsent = await notifications.ListUnsentByReferenceAsync(types, referenceId, cancellationToken);
        foreach (var notification in unsent)
        {
            notification.Cancel();
        }

        return unsent.Count;
    }

    private static string Truncate(string value)
        => value.Length <= Notification.BodyMaxLength ? value : value[..(Notification.BodyMaxLength - 1)] + "…";
}
