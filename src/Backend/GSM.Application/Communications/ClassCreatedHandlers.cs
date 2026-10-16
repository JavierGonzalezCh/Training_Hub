using System.Globalization;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Communications.Abstractions;
using GSM.Domain.Classes;
using GSM.Domain.Communications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

/// <summary>
/// C8: al crear una clase (y si el staff no desmarcó "Avisar a los miembros") publica en Novedades "Nueva clase:
/// ...". La audiencia es todo el gym (miembros activos); Notifications manda el push al consumir
/// <c>AnnouncementPublished</c> y respeta la preferencia «Novedades del gym» de cada miembro: quien la apagó no
/// recibe el push, pero la publicación queda en el feed. Idempotente: el anuncio lleva un id derivado de la
/// clase, así que repetir el evento no lo duplica. Fija el gym del evento en todo (un reintento corre sin usuario).
/// </summary>
public sealed class ClassCreatedPublishesAnnouncement(
    IAnnouncementRepository announcements,
    AnnouncementPublisher publisher,
    IUnitOfWork unitOfWork,
    ILogger<ClassCreatedPublishesAnnouncement> logger) : IDomainEventHandler<ClassCreated>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(ClassCreated domainEvent, CancellationToken cancellationToken = default)
    {
        var id = AnnouncementIdFor(domainEvent.ClassId);
        if (await announcements.GetAsync(id, cancellationToken) is not null)
        {
            return;
        }

        var title = $"Nueva clase: {domainEvent.Name}";
        if (title.Length > Announcement.TitleMaxLength)
        {
            title = title[..Announcement.TitleMaxLength];
        }

        var announcement = Announcement.CreateForNewClass(
            id, domainEvent.GymId, domainEvent.CreatedByUserId ?? Guid.Empty, title, BuildBody(domainEvent),
            // Una clase de una sola fecha deja de ser novedad ese día; la recurrente no caduca.
            domainEvent.IsRecurring ? null : domainEvent.ScheduledDate);
        announcements.Add(announcement);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var reached = await publisher.PublishAsync(announcement, cancellationToken);
        logger.LogInformation(
            "Clase {ClassId} nueva: anuncio {AnnouncementId} publicado ({Reached} destinatarios)",
            domainEvent.ClassId, id, reached);
    }

    /// <summary>Id estable del anuncio de una clase: el mismo evento, el mismo anuncio.</summary>
    internal static Guid AnnouncementIdFor(Guid classId)
        => new(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{classId:N}:class-created")));

    internal static string BuildBody(ClassCreated e)
    {
        var when = e.IsRecurring
            ? $"{DaysText(e.DaysOfWeek)} {Time(e.StartTime)}"
            : $"{e.ScheduledDate?.ToString("dddd d 'de' MMMM", Colombia)} {Time(e.StartTime)}";
        var body = when.Trim();
        if (!string.IsNullOrWhiteSpace(e.InstructorName))
        {
            body += $" · con {e.InstructorName.Trim()}";
        }

        return char.ToUpper(body[0], Colombia) + body[1..];
    }

    private static string Time(TimeOnly time) => time.ToString("h:mm tt", Colombia);

    private static string DaysText(IReadOnlyList<ClassDayOfWeek> days)
    {
        var names = days.Distinct().OrderBy(d => d).Select(DayName).ToList();
        return names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " y " + names[^1],
        };
    }

    private static string DayName(ClassDayOfWeek day) => day switch
    {
        ClassDayOfWeek.Monday => "lunes",
        ClassDayOfWeek.Tuesday => "martes",
        ClassDayOfWeek.Wednesday => "miércoles",
        ClassDayOfWeek.Thursday => "jueves",
        ClassDayOfWeek.Friday => "viernes",
        ClassDayOfWeek.Saturday => "sábado",
        _ => "domingo",
    };
}
