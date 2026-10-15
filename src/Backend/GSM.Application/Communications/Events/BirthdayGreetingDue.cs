using GSM.Application.Common.Events;

namespace GSM.Application.Communications.Events;

/// <summary>
/// Toca felicitar a un miembro por su cumpleaños (Notifications §4.3, Comunicaciones §F). Lo publicará
/// Comunicaciones (CO5) la noche anterior; Notifications lo programa para <paramref name="SendAt"/> (las 8:00
/// del gym) y lo deja cancelable. Idempotente por miembro y día: repetirlo no duplica la felicitación.
/// </summary>
/// <param name="Date">El día del cumpleaños (en años no bisiestos, el 28 de febrero para quien nació el 29).</param>
/// <param name="Title">Título del aviso.</param>
/// <param name="Body">Texto ya armado con el nombre, y el regalo si el gym lo configuró.</param>
/// <param name="SendAt">Instante UTC en que debe salir.</param>
public sealed record BirthdayGreetingDue(
    Guid GymId,
    Guid MemberId,
    DateOnly Date,
    string Title,
    string Body,
    DateTimeOffset SendAt) : IDomainEvent;
