using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Members.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Attendance;

/// <summary>Resultado del intento: entró, o el teclado del equipo está bloqueado hasta <see cref="LockedUntil"/>.</summary>
public sealed record KioskDocumentEntryOutcome(KioskDocumentEntryResponse? Entry, DateTimeOffset? LockedUntil);

/// <summary>
/// Entrada con documento en el kiosco (A4, Attendance §7). Solo la cuenta de dispositivo. Mismas reglas que el
/// QR (las aplica <see cref="CheckInHandler"/>). Cualquier rechazo —documento que no existe, de otro gym,
/// ambiguo, miembro de baja, membresía vencida, ya entró hoy, gym cerrado— responde lo mismo: 409 con
/// <see cref="GenericRejection"/>, para no revelar si el documento existe ni por qué no pasó; el motivo real queda
/// en el intento rechazado (campana del staff). Cinco fallos seguidos bloquean el teclado un minuto.
/// </summary>
public sealed class KioskDocumentEntryHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    ICheckinSettingsRepository settingsRepository,
    DocumentEntryGuard guard,
    CheckInHandler checkIn,
    ILogStore logStore,
    ILogger<KioskDocumentEntryHandler> logger)
{
    public const string GenericRejection = "No pudimos registrar tu entrada. Comunícate con un asesor.";

    public async Task<KioskDocumentEntryOutcome> HandleAsync(
        KioskDocumentEntryRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var deviceId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("Sin dispositivo en el contexto.");

        var settings = await settingsRepository.GetAsync(gymId, cancellationToken) ?? CheckinSettings.Default(gymId);
        if (!settings.ByDocument)
        {
            throw new ForbiddenDomainException("La entrada con documento no está activada en este gimnasio.");
        }

        if (guard.LockedUntil(deviceId, clock.UtcNow) is { } locked)
        {
            return new KioskDocumentEntryOutcome(null, locked);
        }

        try
        {
            var found = await members.FindByDocumentNumberAsync(request.Document, cancellationToken);

            // Cero coincidencias (no existe o es de otro gym: el filtro de tenant no lo deja ver) o más de una
            // (mismo número con otro tipo de documento): no se adivina quién es.
            if (found.Count != 1)
            {
                throw new ConflictException(GenericRejection);
            }

            var response = await checkIn.HandleDocumentAsync(found[0], cancellationToken);
            guard.RegisterSuccess(deviceId);
            logger.LogInformation(
                "Entrada con documento de miembro {MemberId} en el kiosco {KioskId}", response.MemberId, deviceId);

            var expiringSoon = response.Notice is not null || response.GraceDaysLeft is not null;
            return new KioskDocumentEntryOutcome(
                new KioskDocumentEntryResponse(found[0].FirstName, found[0].PhotoUrl, expiringSoon), null);
        }
        catch (ConflictException)
        {
            // Cualquier rechazo (CheckInHandler ya dejó el intento con su motivo) se contesta igual.
            if (guard.RegisterFailure(deviceId, clock.UtcNow))
            {
                // Sin el documento intentado: solo qué equipo.
                logStore.RecordSecurityEvent(new SecurityEventLogEntry(
                    SecurityEventTypes.DocumentEntryLocked, $"kiosk:{deviceId}"));
                logger.LogWarning(
                    "Teclado del kiosco {KioskId} bloqueado tras {Max} intentos fallidos",
                    deviceId, DocumentEntryGuard.MaxFailures);
            }

            throw new ConflictException(GenericRejection);
        }
    }
}
